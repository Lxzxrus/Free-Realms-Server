#!/usr/bin/env python3
"""
Serves the frozen asset copy (evergrove-asset-mirror.py) to game clients over plain HTTP.

Why not nginx or Caddy: FreeRealms.exe puts asset names into its request line unencoded, and 479 of the copied
assets have spaces in their names ("GET /assets/010/Grave Elemental_Teeth Chatter_4.mp3.z?123 HTTP/1.1"). Both
answer that with 400. This reads the request line the way the Cloudflare front of OSFR's server does: the method up
to the first space, the version after the last, and the target in between.

What it serves is fixed at start: the files listed in SHA256SUMS, looked up in a table by their URL. A request never
becomes a filesystem path, so nothing else on the machine can be read. GET and HEAD only, no bodies, bounded request
size, timeouts, and a cap on connections per address and in total. No per-request logging (it would record players'
addresses). SIGHUP reloads the table after the copy changes.

Usage: evergrove-asset-server.py <asset folder> [--port 8080] [--prefix /assets]
"""

import argparse
import asyncio
import os
import signal
import sys
import urllib.parse

MAX_HEAD = 8192
HEAD_TIMEOUT = 10
IDLE_TIMEOUT = 15
MAX_REQUESTS_PER_CONNECTION = 1000
MAX_CONNECTIONS = 1000
MAX_CONNECTIONS_PER_ADDRESS = 32
REPORTS = {"SHA256SUMS", "MISSING.txt", "BAD.txt", "FAILED.txt", "SKIPPED-CODE.txt"}


def load_table(folder, prefix):
    """URL path -> (file path, size), for every file SHA256SUMS lists."""
    table = {}
    with open(os.path.join(folder, "SHA256SUMS"), encoding="utf-8") as sums:
        for line in sums:
            relative = line.rstrip("\n")[66:]
            if not relative or relative in REPORTS:
                continue
            path = os.path.join(folder, *relative.split("/"))
            try:
                size = os.path.getsize(path)
            except OSError:
                continue
            table[f"{prefix}/{relative}"] = (path, size)
    return table


def parse_request_line(line):
    """(method, target, version), or None. The target may contain spaces."""
    first = line.find(" ")
    last = line.rfind(" ")
    if first <= 0 or last <= first:
        return None
    method, target, version = line[:first], line[first + 1:last], line[last + 1:]
    if not version.startswith("HTTP/1.") or not target.startswith("/"):
        return None
    return method, target, version


class AssetServer:
    def __init__(self, folder, prefix):
        self.folder = folder
        self.prefix = prefix
        self.table = load_table(folder, prefix)
        self.connections = 0
        self.per_address = {}

    def reload(self):
        try:
            self.table = load_table(self.folder, self.prefix)
            print(f"Reloaded: {len(self.table)} files.", flush=True)
        except OSError as error:
            print(f"Reload failed, keeping the old table: {error}", flush=True)

    def lookup(self, target):
        path = target.split("?", 1)[0]
        found = self.table.get(path)
        if found is None and "%" in path:
            found = self.table.get(urllib.parse.unquote(path, errors="strict"))
        return found

    async def handle(self, reader, writer):
        address = (writer.get_extra_info("peername") or ("?",))[0]

        if self.connections >= MAX_CONNECTIONS or self.per_address.get(address, 0) >= MAX_CONNECTIONS_PER_ADDRESS:
            writer.close()
            return

        self.connections += 1
        self.per_address[address] = self.per_address.get(address, 0) + 1

        try:
            for served in range(MAX_REQUESTS_PER_CONNECTION):
                timeout = HEAD_TIMEOUT if served == 0 else IDLE_TIMEOUT
                try:
                    head = await asyncio.wait_for(reader.readuntil(b"\r\n\r\n"), timeout)
                except (asyncio.IncompleteReadError, asyncio.LimitOverrunError, asyncio.TimeoutError, ConnectionError):
                    return

                try:
                    if not await self.respond(head, writer):
                        return
                except (ConnectionError, OSError):
                    return
        finally:
            self.connections -= 1
            remaining = self.per_address[address] - 1
            if remaining:
                self.per_address[address] = remaining
            else:
                del self.per_address[address]
            writer.close()

    async def respond(self, head, writer):
        """Answers one request. False when the connection should close."""
        try:
            text = head.decode("latin-1")
        except UnicodeDecodeError:
            return await self.send_error(writer, 400, "Bad Request")

        lines = text.split("\r\n")
        request = parse_request_line(lines[0])
        if request is None:
            return await self.send_error(writer, 400, "Bad Request")

        method, target, version = request
        headers = {}
        for line in lines[1:]:
            name, colon, value = line.partition(":")
            if colon:
                headers[name.strip().lower()] = value.strip()

        if headers.get("content-length", "0") != "0" or "transfer-encoding" in headers:
            return await self.send_error(writer, 400, "Bad Request")

        if method not in ("GET", "HEAD"):
            return await self.send_error(writer, 405, "Method Not Allowed", allow=True)

        try:
            found = self.lookup(target)
        except UnicodeDecodeError:
            found = None

        if found is None:
            return await self.send_error(writer, 404, "Not Found",
                                         keep_alive=self.keep_alive(version, headers))

        path, size = found
        keep_alive = self.keep_alive(version, headers)

        try:
            file = open(path, "rb")
        except OSError:
            return await self.send_error(writer, 404, "Not Found")

        # Streamed from the file (sendfile where the OS has it), never held whole in memory.
        with file:
            writer.write((
                "HTTP/1.1 200 OK\r\n"
                "Content-Type: application/octet-stream\r\n"
                f"Content-Length: {size}\r\n"
                "Cache-Control: public, max-age=86400\r\n"
                f"Connection: {'keep-alive' if keep_alive else 'close'}\r\n\r\n").encode("latin-1"))
            await writer.drain()

            if method == "GET":
                await asyncio.get_running_loop().sendfile(writer.transport, file, 0, size)

        return keep_alive

    @staticmethod
    def keep_alive(version, headers):
        connection = headers.get("connection", "").lower()
        return connection != "close" if version == "HTTP/1.1" else connection == "keep-alive"

    @staticmethod
    async def send_error(writer, status, reason, keep_alive=False, allow=False):
        writer.write((
            f"HTTP/1.1 {status} {reason}\r\n"
            + ("Allow: GET, HEAD\r\n" if allow else "")
            + "Content-Length: 0\r\n"
            f"Connection: {'keep-alive' if keep_alive else 'close'}\r\n\r\n").encode("latin-1"))
        try:
            await writer.drain()
        except ConnectionError:
            return False
        return keep_alive


async def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("folder")
    parser.add_argument("--port", type=int, default=8080)
    parser.add_argument("--prefix", default="/assets")
    args = parser.parse_args()

    server = AssetServer(os.path.abspath(args.folder), args.prefix.rstrip("/"))
    print(f"{len(server.table)} files under {args.prefix}/ on port {args.port}.", flush=True)

    if hasattr(signal, "SIGHUP"):
        asyncio.get_running_loop().add_signal_handler(signal.SIGHUP, server.reload)

    listener = await asyncio.start_server(server.handle, host=None, port=args.port, limit=MAX_HEAD,
                                          reuse_address=True)
    async with listener:
        await listener.serve_forever()


if __name__ == "__main__":
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        sys.exit(0)
