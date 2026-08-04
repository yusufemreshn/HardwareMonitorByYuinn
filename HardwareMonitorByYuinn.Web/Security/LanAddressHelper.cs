using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace HardwareMonitorByYuinn.Web.Security;

/// <summary>
/// Bu bilgisayarın yerel ağdaki IPv4 adreslerini bulur. Hem RemoteAccessController'ın "Yerel
/// ağdan erişim adresi" gösterimi hem de SelfSignedCertificateProvider'ın sertifikaya hangi
/// IP'leri (SAN) ekleyeceği aynı listeye ihtiyaç duyduğundan tek yerden tanımlanır.
/// </summary>
internal static class LanAddressHelper
{
    public static List<string> GetIPv4Addresses()
    {
        var addresses = new List<string>();
        foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up)
            {
                continue;
            }

            foreach (UnicastIPAddressInformation addressInfo in nic.GetIPProperties().UnicastAddresses)
            {
                if (addressInfo.Address.AddressFamily == AddressFamily.InterNetwork
                    && !IPAddress.IsLoopback(addressInfo.Address))
                {
                    addresses.Add(addressInfo.Address.ToString());
                }
            }
        }

        return addresses;
    }
}
