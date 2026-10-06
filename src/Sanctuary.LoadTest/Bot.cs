using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Threading;

using Sanctuary.LoadTest.Net;
using Sanctuary.LoadTest.Protocol;
using Sanctuary.Packet;
using Sanctuary.UdpLibrary.Enumerations;
using Sanctuary.UdpLibrary.Statistics;

namespace Sanctuary.LoadTest;

public enum BotState
{
    Offline,
    LoginConnecting,
    LoginAuthenticating,
    ListingCharacters,
    CreatingCharacter,
    SelectingCharacter,
    GatewayConnecting,
    GatewayAuthenticating,
    Loading,
    InWorld,
    Stopping,
    Stopped,
    Failed
}

/// <summary>
/// One fake player. Everything that touches its sockets runs on the worker thread that pumps it;
/// other threads talk to it through <see cref="Post"/>.
/// </summary>
public sealed class Bot : IBotConnectionHandler
{
    private const float GroundY = -39.7098f; // the zone's spawn height; the server doesn't check heights

    private static readonly long LoginTimeout = Stopwatch.Frequency * 60;
    private static readonly long LoadingTime = Stopwatch.Frequency; // a real client spends this loading the zone
    private static readonly long RetryDelay = Stopwatch.Frequency * 3;

    private readonly LoadTestOptions _options;
    private readonly IServiceProvider _serviceProvider;
    private readonly MoveTracker _moveTracker;
    private readonly RunCounters _counters;
    private readonly Random _random;
    private readonly ConcurrentQueue<Action> _inbox = new();

    private BotUdpManager? _loginManager;
    private BotConnection? _login;
    private bool _closeLoginPending;
    private volatile BotUdpManager? _gatewayManager;
    private BotConnection? _gateway;

    private string? _session;
    private string? _ticket;
    private string? _gatewayAddress;
    private string _characterName;
    private int _nameAttempts;
    private int _selectAttempts;
    private long _stateSince;
    private long _retryAt;

    // Movement, in world units on the X/Z plane.
    private Vector2 _home;
    private float _radius;
    private Vector2 _position;
    private Vector2 _waypoint;
    private Vector2 _facing = Vector2.UnitY;
    private bool _walking;
    private long _idleUntil;
    private long _nextMoveAt;
    private long _nextJumpAt;
    private long _nextChatAt;
    private int _chatLines;

    private readonly object _seenLock = new();
    private readonly HashSet<ulong> _seenSenders = [];

    public int Index { get; }
    public string Username { get; }
    public ulong Guid { get; private set; }

    private volatile BotState _state;
    public BotState State => _state;

    public string? FailureReason { get; private set; }

    public Bot(int index, LoadTestOptions options, IServiceProvider serviceProvider, MoveTracker moveTracker, RunCounters counters)
    {
        Index = index;
        Username = $"{options.AccountPrefix}{index:D3}";
        _characterName = $"Loadbot{index:D3}";
        _options = options;
        _serviceProvider = serviceProvider;
        _moveTracker = moveTracker;
        _counters = counters;
        _random = new Random(HashCode.Combine(options.Seed, index));
    }

    public void Post(Action action) => _inbox.Enqueue(action);

    /// <summary>Begins logging in with a WebAPI session. Call through <see cref="Post"/>.</summary>
    public void Start(string session)
    {
        _session = session;

        _loginManager = new BotUdpManager(BotUdpManager.CreateParams("LoginUdp_6"), _serviceProvider);
        _login = _loginManager.EstablishConnection(_options.Host, _options.LoginPort, timeout: 10000);

        if (_login is null)
        {
            Fail($"could not reach Login at {_options.Host}:{_options.LoginPort}", isLogin: true);
            return;
        }

        _login.Handler = this;
        _login.UseLoginCipher();

        SetState(BotState.LoginConnecting);
    }

    /// <summary>Moves the bot to a new home and walking radius. Call through <see cref="Post"/>.</summary>
    public void SetHome(Vector2 home, float radius)
    {
        _home = home;
        _radius = radius;

        if (_state == BotState.InWorld)
            Teleport();
    }

    /// <summary>Logs the character out. Call through <see cref="Post"/>.</summary>
    public void Stop()
    {
        if (_gateway is not null && _gateway.Status != Status.Disconnected)
        {
            SetState(BotState.Stopping);
            _gateway.Disconnect();
        }

        CloseLogin();
        CloseGateway();

        if (_state != BotState.Failed)
            SetState(BotState.Stopped);
    }

    public bool TryGetGatewayStats(out UdpManagerStatistics stats)
    {
        var manager = _gatewayManager;

        if (manager is null)
        {
            stats = default;
            return false;
        }

        manager.GetStats(out stats);
        return true;
    }

    /// <summary>How many other bots this one has seen move since the last call.</summary>
    public int TakeSeenSenders()
    {
        lock (_seenLock)
        {
            var count = _seenSenders.Count;
            _seenSenders.Clear();
            return count;
        }
    }

    /// <summary>Gives the bot time on its worker thread. Returns true if any packet arrived.</summary>
    public bool Pump(long now)
    {
        while (_inbox.TryDequeue(out var action))
            action();

        var hadData = false;

        if (_loginManager is not null)
            hadData |= _loginManager.GiveTime(5);

        // Closed here rather than in the packet callback, which runs inside GiveTime while it still reads the socket.
        if (_closeLoginPending)
            CloseLoginNow();

        if (_gatewayManager is not null)
            hadData |= _gatewayManager.GiveTime(5);

        switch (_state)
        {
            case BotState.SelectingCharacter when _retryAt != 0 && now >= _retryAt:
                _retryAt = 0;
                _login?.SendReliable(ClientPackets.CharacterLoginRequest(Guid));
                break;

            case BotState.Loading when now - _stateSince >= LoadingTime:
                _gateway!.SendReliable(ClientPackets.ClientFinishedLoading());
                SetState(BotState.InWorld);
                Teleport();
                _nextChatAt = now + Exponential(_options.MeanChatSeconds);
                break;

            case BotState.InWorld:
                Behave(now);
                break;
        }

        if (_state is > BotState.Offline and < BotState.InWorld && now - _stateSince > LoginTimeout)
            Fail($"stuck in {_state} for 60 s", isLogin: true);

        return hadData;
    }

    #region Behaviour

    private void Teleport()
    {
        _position = _home + RandomInCircle(_radius);
        _walking = false;
        _idleUntil = Stopwatch.GetTimestamp() + Exponential(_options.MeanIdleSeconds);

        SendMove(Stopwatch.GetTimestamp());
    }

    private void Behave(long now)
    {
        if (now >= _nextChatAt)
        {
            _gateway!.SendReliable(ClientPackets.Say($"load test line {++_chatLines} from bot {Index}"));
            Interlocked.Increment(ref _counters.ChatsSent);
            _nextChatAt = now + Exponential(_options.MeanChatSeconds);
        }

        if (!_walking)
        {
            if (now < _idleUntil)
                return;

            _walking = true;
            _waypoint = _home + RandomInCircle(_radius);
            _nextMoveAt = now;
            _nextJumpAt = now + Exponential(_options.MeanJumpSeconds);
        }

        if (now < _nextMoveAt)
            return;

        var step = _options.WalkSpeed / (float)_options.MoveHz;
        var toWaypoint = _waypoint - _position;
        var distance = toWaypoint.Length();

        if (distance > 0.001f)
            _facing = toWaypoint / distance;

        if (distance <= step)
        {
            _position = _waypoint;

            // Half the time walk on, otherwise stand around.
            if (_random.NextDouble() < 0.5)
                _waypoint = _home + RandomInCircle(_radius);
            else
            {
                _walking = false;
                _idleUntil = now + Exponential(_options.MeanIdleSeconds);
            }
        }
        else
        {
            _position += _facing * step;
        }

        SendMove(now);

        _nextMoveAt += (long)(Stopwatch.Frequency / _options.MoveHz);

        // Don't try to catch up after a stall; real clients don't burst either.
        if (_nextMoveAt < now)
            _nextMoveAt = now;

        if (_walking && now >= _nextJumpAt)
        {
            _gateway!.SendReliable(ClientPackets.Jump(Guid, Position4, Rotation, verticalVelocity: 8f));
            Interlocked.Increment(ref _counters.JumpsSent);
            _nextJumpAt = now + Exponential(_options.MeanJumpSeconds);
        }
    }

    private Vector4 Position4 => new(_position.X, GroundY, _position.Y, 1f);

    // The game stores a facing as a quaternion with only X and Z set (see GatewayConnection.CreatePlayerFromDatabase).
    private Quaternion Rotation => new(_facing.X, 0f, _facing.Y, 0f);

    private void SendMove(long now)
    {
        _moveTracker.OnSent(Guid, _position.X, _position.Y, now);
        _gateway!.SendReliable(ClientPackets.UpdatePosition(Guid, Position4, Rotation, state: 1));
        Interlocked.Increment(ref _counters.MovesSent);
    }

    private Vector2 RandomInCircle(float radius)
    {
        var angle = _random.NextDouble() * Math.Tau;
        var distance = radius * Math.Sqrt(_random.NextDouble());

        return new Vector2((float)(Math.Cos(angle) * distance), (float)(Math.Sin(angle) * distance));
    }

    private long Exponential(double meanSeconds)
    {
        return (long)(-Math.Log(1.0 - _random.NextDouble()) * meanSeconds * Stopwatch.Frequency);
    }

    #endregion

    #region Connection callbacks

    public void OnConnectComplete(BotConnection connection)
    {
        if (connection == _login)
        {
            SetState(BotState.LoginAuthenticating);
            _login.SendReliable(ClientPackets.LoginRequest(_session!));
        }
        else if (connection == _gateway)
        {
            SetState(BotState.GatewayAuthenticating);
            _gateway.SendReliable(ClientPackets.PacketLogin(_ticket!, Guid, _options.ClientVersion));
        }
    }

    public void OnPacket(BotConnection connection, ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            return;

        if (connection == _login)
            OnLoginPacket(data);
        else if (connection == _gateway)
            OnGatewayPacket(data);
    }

    private void OnLoginPacket(ReadOnlySpan<byte> data)
    {
        switch (data[0])
        {
            case LoginReply.OpCode:
                if (!ClientPackets.TryReadLoginReply(data, out var loggedIn, out var status) || !loggedIn)
                {
                    Fail($"Login refused the session (status {status})", isLogin: true);
                    return;
                }

                SetState(BotState.ListingCharacters);
                _login!.SendReliable(ClientPackets.CharacterSelectInfoRequest());
                break;

            case CharacterSelectInfoReply.OpCode:
                if (!ClientPackets.TryReadCharacterSelectInfoReply(data, out var guids))
                {
                    Fail("unreadable character list", isLogin: true);
                    return;
                }

                if (guids.Count > 0)
                    SelectCharacter(guids[0]);
                else
                    CreateCharacter();
                break;

            case CharacterCreateReply.OpCode:
                if (!ClientPackets.TryReadCharacterCreateReply(data, out var result, out var guid))
                {
                    Fail("unreadable character create reply", isLogin: true);
                    return;
                }

                if (result == 1)
                    SelectCharacter(guid);
                else if (result == 4 && ++_nameAttempts < 5)
                {
                    // Name taken, perhaps by a bot account with a different prefix.
                    _characterName = $"Loadbot{Index:D3}{(char)('a' + _random.Next(26))}{(char)('a' + _random.Next(26))}";
                    CreateCharacter();
                }
                else
                    Fail($"character create refused (result {result})", isLogin: true);
                break;

            case CharacterLoginReply.OpCode:
                if (!ClientPackets.TryReadCharacterLoginReply(data, out var loginStatus, out var serverAddress, out var ticket))
                {
                    Fail("unreadable character login reply", isLogin: true);
                    return;
                }

                // 8: the character is still online from an earlier run; the Gateway logs it out within seconds.
                if (loginStatus == 8 && ++_selectAttempts < 10)
                {
                    _retryAt = Stopwatch.GetTimestamp() + RetryDelay;
                    return;
                }

                if (loginStatus != 1)
                {
                    Fail($"character login refused (status {loginStatus})", isLogin: true);
                    return;
                }

                _ticket = ticket;
                _gatewayAddress = _options.GatewayAddress ?? serverAddress;

                CloseLogin();
                ConnectGateway();
                break;
        }
    }

    private void CreateCharacter()
    {
        SetState(BotState.CreatingCharacter);
        _login!.SendReliable(ClientPackets.CharacterCreateRequest(_characterName));
    }

    private void SelectCharacter(ulong guid)
    {
        Guid = guid;
        SetState(BotState.SelectingCharacter);
        _login!.SendReliable(ClientPackets.CharacterLoginRequest(guid));
    }

    private void ConnectGateway()
    {
        _gatewayManager = new BotUdpManager(BotUdpManager.CreateParams("CGAPI_527"), _serviceProvider);
        _gateway = _gatewayManager.EstablishConnection(_gatewayAddress!, timeout: 10000);

        if (_gateway is null)
        {
            Fail($"could not reach the Gateway at {_gatewayAddress}", isLogin: true);
            return;
        }

        _gateway.Handler = this;

        SetState(BotState.GatewayConnecting);
    }

    private void OnGatewayPacket(ReadOnlySpan<byte> data)
    {
        if (data.Length < 2)
            return;

        var opCode = BinaryPrimitives.ReadInt16LittleEndian(data);

        if (opCode == PacketLoginReply.OpCode)
        {
            if (!ClientPackets.TryReadPacketLoginReply(data, out var success) || !success)
            {
                Fail("Gateway refused the ticket", isLogin: true);
                return;
            }

            SetState(BotState.Loading);
            _gateway!.SendReliable(ClientPackets.ClientIsReady());
            return;
        }

        // PacketTunneledClientPacket: opcode (2), reliable (1), payload size (4), payload.
        if (opCode != PacketTunneledClientPacket.OpCode || data.Length < 9)
            return;

        var payload = data[7..];

        switch (BinaryPrimitives.ReadInt16LittleEndian(payload))
        {
            case PlayerUpdatePacketUpdatePosition.OpCode:
                OnPositionSeen(payload);
                break;

            case BaseChatPacket.OpCode when payload.Length > 4 && BinaryPrimitives.ReadInt16LittleEndian(payload[2..]) == PacketChat.OpCode:
                if (PacketChat.TryDeserialize(payload, out var chat) && chat.FromGuid != Guid)
                    Interlocked.Increment(ref _counters.ChatsSeen);
                break;
        }
    }

    private void OnPositionSeen(ReadOnlySpan<byte> payload)
    {
        var now = Stopwatch.GetTimestamp();

        // opcode (2), guid (8), x, y, z (4 each), ...
        if (payload.Length < 22)
            return;

        var sender = BinaryPrimitives.ReadUInt64LittleEndian(payload[2..]);

        if (sender == Guid)
            return;

        var x = BinaryPrimitives.ReadSingleLittleEndian(payload[10..]);
        var z = BinaryPrimitives.ReadSingleLittleEndian(payload[18..]);

        if (_moveTracker.OnSeen(sender, x, z, now))
        {
            Interlocked.Increment(ref _counters.MovesSeen);

            lock (_seenLock)
                _seenSenders.Add(sender);
        }
    }

    public void OnTerminated(BotConnection connection, DisconnectReason reason)
    {
        if (connection == _login)
        {
            if (_state < BotState.GatewayConnecting)
                Fail($"Login connection closed: {reason}", isLogin: true);
        }
        else if (connection == _gateway)
        {
            if (_state < BotState.InWorld)
                Fail($"Gateway connection closed while logging in: {reason}", isLogin: true);
            else if (_state == BotState.InWorld)
                Fail($"Gateway connection closed: {reason}", isLogin: false);
        }
    }

    public void OnCorrupt(BotConnection connection, string what)
    {
        Interlocked.Increment(ref _counters.CorruptPackets);
        Console.Error.WriteLine($"[{Username}] corrupt packet ({what}) on the {(connection == _login ? "Login" : "Gateway")} connection");
    }

    #endregion

    private void SetState(BotState state)
    {
        _state = state;
        _stateSince = Stopwatch.GetTimestamp();
    }

    private void Fail(string reason, bool isLogin)
    {
        if (_state is BotState.Failed or BotState.Stopping or BotState.Stopped)
            return;

        FailureReason = reason;
        SetState(BotState.Failed);

        if (isLogin)
            Interlocked.Increment(ref _counters.LoginFailures);
        else
            Interlocked.Increment(ref _counters.Disconnects);

        Console.Error.WriteLine($"[{Username}] {reason}");

        CloseLogin();
    }

    private void CloseLogin()
    {
        if (_login is not null)
            _login.Handler = null;

        _closeLoginPending = true;
    }

    private void CloseLoginNow()
    {
        _closeLoginPending = false;

        var manager = _loginManager;
        var connection = _login;

        _loginManager = null;
        _login = null;

        connection?.Disconnect();

        if (manager is not null)
        {
            manager.GiveTime(0);
            manager.Dispose();
        }
    }

    private void CloseGateway()
    {
        var manager = _gatewayManager;
        var connection = _gateway;

        _gatewayManager = null;
        _gateway = null;

        if (connection is not null)
            connection.Handler = null;

        if (manager is not null)
        {
            manager.GiveTime(0);
            manager.Dispose();
        }
    }
}
