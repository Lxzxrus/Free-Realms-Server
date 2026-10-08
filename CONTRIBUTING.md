# Contributing

Bug reports and ideas are welcome as [issues](https://github.com/Lxzxrus/Free-Realms-Server/issues), and code as pull
requests to `main`.

1. Fork the repository and create a branch for your change.
2. Build and test: `cd src && dotnet restore && dotnet build --no-restore && dotnet test --no-build` (the launcher:
   `cd launcher && dotnet test src/Launcher.slnx`). CI runs the same.
3. Open a pull request that says what changed, why, and what you checked **in the game client**: passing tests
   don't show that the client accepts a packet.

## Coding values

- **Match the surrounding code:** style, naming and folder structure.
- **Reuse before adding:** look for something in the codebase that already does it, and avoid a second source of
  truth.
- **Prefer smaller changes:** a pull request that can be tested on its own is reviewed sooner.
- **Never commit game client files or extracted assets**, and never secrets: real passwords, keys and the
  Login/Gateway challenge belong in the git-ignored local settings files.

## AI-assisted work

This project is developed with AI assistance: Claude is a coding partner, and its commits say so. AI-assisted
contributions are welcome on the same terms as any other: a person understands the change, has tested it, and
answers for it in review.

## Security

Please don't open public issues for security problems. Send a direct message to Nate on the
[Discord server](https://discord.gg/5kX5hh7skx) instead, and give it time to be fixed before telling anyone else.
