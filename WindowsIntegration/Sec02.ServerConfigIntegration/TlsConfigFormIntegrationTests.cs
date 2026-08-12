using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Xunit;

namespace Sec02.ServerConfigIntegration.Windows;

public sealed class TlsConfigFormIntegrationTests
{
    [Fact]
    public void 真实服务端配置表单加载TLS控件并拒绝无效证书路径()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                RunFormAssertions();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "TLS 配置表单宿主测试超时。");
        Assert.Null(failure);
    }

    private static void RunFormAssertions()
    {
        var original = (Server.Settings.TlsEnabled, Server.Settings.TlsPort,
            Server.Settings.AllowLegacyV1, Server.Settings.TlsCertificatePath, Server.Settings.Port);
        string certificatePath = Path.GetTempFileName();
        try
        {
            using (RSA rsa = RSA.Create(2048))
            {
                var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                using X509Certificate2 generatedCertificate = request.CreateSelfSigned(
                    DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
                File.WriteAllBytes(certificatePath, generatedCertificate.Export(X509ContentType.Pkcs12, string.Empty));
            }

            Server.Settings.TlsEnabled = true;
            Server.Settings.TlsPort = 7001;
            Server.Settings.AllowLegacyV1 = false;
            Server.Settings.TlsCertificatePath = certificatePath;
            Server.Settings.Port = 7000;

            using var form = new Server.ConfigForm();
            var tlsGroup = Assert.Single(form.Controls.Find("TlsGroupBox", true));
            Assert.IsType<GroupBox>(tlsGroup);
            var enabled = Assert.IsType<CheckBox>(Assert.Single(form.Controls.Find("TlsEnabledCheckBox", true)));
            var tlsPort = Assert.IsType<TextBox>(Assert.Single(form.Controls.Find("TlsPortTextBox", true)));
            var certificate = Assert.IsType<TextBox>(Assert.Single(form.Controls.Find("TlsCertificatePathTextBox", true)));
            Assert.True(enabled.Checked);
            Assert.Equal("7001", tlsPort.Text);
            Assert.Equal(certificatePath, certificate.Text);

            tlsPort.Text = "7443";
            Assert.True(form.TrySave(out string validError), validError);
            Assert.Equal((ushort)7443, Server.Settings.TlsPort);

            certificate.Text = certificatePath + ".missing";
            Assert.False(form.TrySave(out string invalidError));
            Assert.Contains("不存在", invalidError);
            Assert.Equal(certificatePath, Server.Settings.TlsCertificatePath);

            enabled.Checked = false;
            tlsPort.Text = "not-a-port";
            Assert.True(form.TrySave(out string disabledError), disabledError);
            Assert.False(Server.Settings.TlsEnabled);
            Assert.Equal((ushort)7443, Server.Settings.TlsPort);
        }
        finally
        {
            (Server.Settings.TlsEnabled, Server.Settings.TlsPort,
                Server.Settings.AllowLegacyV1, Server.Settings.TlsCertificatePath, Server.Settings.Port) = original;
            File.Delete(certificatePath);
        }
    }
}
