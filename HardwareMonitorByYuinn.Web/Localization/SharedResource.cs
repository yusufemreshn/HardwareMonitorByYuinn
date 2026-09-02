namespace HardwareMonitorByYuinn.Web.Localization;

/// <summary>
/// Marker type only; its namespace/name anchors the shared resx files under
/// Resources/Localization/SharedResource.{culture}.resx (see AddLocalization's ResourcesPath in
/// Program.cs). Türkçe metnin kendisi anahtar olarak kullanılır (bkz. IStringLocalizer kullanımı
/// view'larda); bu yüzden varsayılan kültür (tr-TR) için ayrı bir resx dosyasına gerek yoktur,
/// yalnızca "SharedResource.en.resx" çeviri sağlar.
/// </summary>
public class SharedResource
{
}
