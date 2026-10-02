using System;
using System.Net;
using DohCloudflare;
using Xunit;

namespace DohCloudflare.Tests
{
    public class DohManagerTests
    {
        [Fact]
        public void DnsServers_ContainsValidCloudflareIps()
        {
            Assert.Equal(2, DohManager.DnsServers.Length);
            Assert.Equal("1.1.1.1", DohManager.DnsServers[0]);
            Assert.Equal("1.0.0.1", DohManager.DnsServers[1]);

            foreach (var ipStr in DohManager.DnsServers)
            {
                Assert.True(IPAddress.TryParse(ipStr, out var ip));
                Assert.Equal(System.Net.Sockets.AddressFamily.InterNetwork, ip.AddressFamily);
            }
        }

        [Fact]
        public void DohTemplate_IsValidHttpsUrl()
        {
            Assert.StartsWith("https://", DohManager.DohTemplate);
            Assert.True(Uri.TryCreate(DohManager.DohTemplate, UriKind.Absolute, out var uri));
            Assert.Equal("cloudflare-dns.com", uri.Host);
            Assert.Equal("/dns-query", uri.AbsolutePath);
        }

        [Theory]
        [InlineData(22000, true)]
        [InlineData(22621, true)]
        [InlineData(22631, true)]
        [InlineData(19045, false)]
        [InlineData(17763, false)]
        public void IsWindows11OrHigher_EvaluatesCorrectlyBasedOnBuild(int buildNumber, bool expectedResult)
        {
            bool result = DohManager.IsWindows11OrHigher(buildNumber);
            Assert.Equal(expectedResult, result);
        }

        [Fact]
        public void IsDnsConfigured_NullOrEmpty_ReturnsFalse()
        {
            Assert.False(DohManager.IsDnsConfigured(null));
            Assert.False(DohManager.IsDnsConfigured(Array.Empty<string>()));
        }

        [Fact]
        public void IsDnsConfigured_MatchingDns_ReturnsTrue()
        {
            var matching = new[] { "1.1.1.1", "1.0.0.1" };
            Assert.True(DohManager.IsDnsConfigured(matching));
        }

        [Fact]
        public void IsDnsConfigured_DifferentDns_ReturnsFalse()
        {
            var other = new[] { "8.8.8.8", "8.8.4.4" };
            Assert.False(DohManager.IsDnsConfigured(other));

            var partial = new[] { "1.1.1.1" };
            Assert.False(DohManager.IsDnsConfigured(partial));

            var reversed = new[] { "1.0.0.1", "1.1.1.1" };
            Assert.False(DohManager.IsDnsConfigured(reversed));
        }

        [Fact]
        public void RegistryPaths_AreWellFormed()
        {
            Assert.Contains("InterfaceSpecificParameters", DohManager.RegistryPathDnscache);
            Assert.Contains("Tcpip\\Parameters\\Interfaces", DohManager.RegistryPathTcpipParams);
            Assert.Equal(22000, DohManager.Windows11Build);
        }
    }
}
