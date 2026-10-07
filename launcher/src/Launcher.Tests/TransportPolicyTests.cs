using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Launcher.Helpers;

namespace Launcher.Tests;

[TestClass]
public class TransportPolicyTests
{
    [TestMethod]
    [DataRow("https://play.example.com")]
    [DataRow("https://203.0.113.5:8443/")]
    [DataRow("http://localhost:8080")]
    [DataRow("http://127.0.0.1:20040/")]
    [DataRow("http://[::1]:20040/")]
    [DataRow("http://192.168.1.20:8080")]
    [DataRow("http://10.0.0.5")]
    [DataRow("http://172.16.0.1")]
    [DataRow("http://172.31.255.255")]
    [DataRow("http://169.254.10.10")]
    [DataRow("http://[fd12:3456::1]/")]
    [DataRow("http://[fe80::1]/")]
    [DataRow("http://[::ffff:192.168.1.20]/")]
    [DataRow("http://optiplex.local:8080")]
    public void Allows_https_and_local_http(string url)
    {
        Assert.IsTrue(TransportPolicy.IsAllowed(url));
    }

    [TestMethod]
    [DataRow("http://play.example.com")]
    [DataRow("http://203.0.113.5")]
    [DataRow("http://172.32.0.1")]
    [DataRow("http://192.169.0.1")]
    [DataRow("http://11.0.0.1")]
    [DataRow("http://[2001:db8::1]/")]
    [DataRow("http://[::ffff:203.0.113.5]/")]
    [DataRow("http://localhost.example.com")]
    [DataRow("http://local")]
    [DataRow("ftp://192.168.1.20/")]
    [DataRow("file:///C:/Windows")]
    [DataRow("not a url")]
    [DataRow("/relative/path")]
    public void Refuses_plain_http_to_the_internet_and_other_schemes(string url)
    {
        Assert.IsFalse(TransportPolicy.IsAllowed(url));
    }

    [TestMethod]
    public async Task Handler_refuses_before_sending()
    {
        var stub = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var client = new HttpClient(new TransportPolicyHandler(stub));

        await Assert.ThrowsExactlyAsync<InsecureTransportException>(() => client.GetAsync("http://play.example.com/servermanifest.xml"));

        Assert.AreEqual(0, stub.Calls);
    }

    [TestMethod]
    public async Task Handler_lets_https_through()
    {
        var stub = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var client = new HttpClient(new TransportPolicyHandler(stub));

        using var response = await client.GetAsync("https://play.example.com/servermanifest.xml");

        Assert.AreEqual(1, stub.Calls);
    }
}
