using HardwareMonitorByYuinn.Web.Localization;

namespace HardwareMonitorByYuinn.Web.Desktop;

/// <summary>
/// Masaüstü kabuğundaki (ShellForm/SplashForm/RepairForm/WebView2RuntimeInstaller) az sayıda metin
/// için basit bir çeviri sözlüğü; bu formlar Razor/IStringLocalizer altyapısının dışında, doğrudan
/// WinForms ile çizildiğinden ayrı bir mekanizma gerekiyor. Anahtar olarak yine Türkçe kaynak metin
/// kullanılır (bkz. Resources/Localization/SharedResource.en.resx'teki aynı desen).
/// </summary>
internal static class Loc
{
    private static readonly Dictionary<string, string> En = new()
    {
        ["Göster"] = "Show",
        ["Çıkış"] = "Exit",
        ["Program başlatılıyor…"] = "Starting up…",
        ["Veritabanı Onarılıyor"] = "Repairing Database",
        ["Geçen seferki beklenmedik kapanmadan kalan bir sorun düzeltiliyor…"] = "Fixing an issue left over from last time's unexpected shutdown…",
        ["{0} bozuk görünüyor, onarılıyor…"] = "{0} looks corrupted, repairing…",
        ["Tamamlandı"] = "Completed",
        ["Bu uygulamanın arayüzü için gereken Microsoft Edge WebView2 bileşeni kurulamadı (muhtemelen internet bağlantısı yok). Lütfen internete bağlanıp uygulamayı tekrar başlatın, ya da https://developer.microsoft.com/microsoft-edge/webview2/consumer/ adresinden elle kurun."]
            = "The Microsoft Edge WebView2 component required for this application's interface couldn't be installed (likely no internet connection). Please connect to the internet and restart the app, or install it manually from https://developer.microsoft.com/microsoft-edge/webview2/consumer/.",
    };

    private static readonly Dictionary<string, string> De = new()
    {
        ["Göster"] = "Anzeigen",
        ["Çıkış"] = "Beenden",
        ["Program başlatılıyor…"] = "Programm wird gestartet…",
        ["Veritabanı Onarılıyor"] = "Datenbank wird repariert",
        ["Geçen seferki beklenmedik kapanmadan kalan bir sorun düzeltiliyor…"] = "Ein Problem vom letzten unerwarteten Beenden wird behoben…",
        ["{0} bozuk görünüyor, onarılıyor…"] = "{0} scheint beschädigt zu sein, wird repariert…",
        ["Tamamlandı"] = "Abgeschlossen",
        ["Bu uygulamanın arayüzü için gereken Microsoft Edge WebView2 bileşeni kurulamadı (muhtemelen internet bağlantısı yok). Lütfen internete bağlanıp uygulamayı tekrar başlatın, ya da https://developer.microsoft.com/microsoft-edge/webview2/consumer/ adresinden elle kurun."]
            = "Die für die Oberfläche dieser Anwendung erforderliche Microsoft Edge WebView2-Komponente konnte nicht installiert werden (wahrscheinlich keine Internetverbindung). Bitte stellen Sie eine Internetverbindung her und starten Sie die App neu, oder installieren Sie die Komponente manuell unter https://developer.microsoft.com/microsoft-edge/webview2/consumer/.",
    };
    private static readonly Dictionary<string, string> Ru = new()
    {
        ["Göster"] = "Показать",
        ["Çıkış"] = "Выход",
        ["Program başlatılıyor…"] = "Запуск программы…",
        ["Veritabanı Onarılıyor"] = "Восстановление базы данных",
        ["Geçen seferki beklenmedik kapanmadan kalan bir sorun düzeltiliyor…"] = "Исправление проблемы, оставшейся после прошлого неожиданного завершения работы…",
        ["{0} bozuk görünüyor, onarılıyor…"] = "{0} повреждён, восстановление…",
        ["Tamamlandı"] = "Готово",
        ["Bu uygulamanın arayüzü için gereken Microsoft Edge WebView2 bileşeni kurulamadı (muhtemelen internet bağlantısı yok). Lütfen internete bağlanıp uygulamayı tekrar başlatın, ya da https://developer.microsoft.com/microsoft-edge/webview2/consumer/ adresinden elle kurun."]
            = "Не удалось установить компонент Microsoft Edge WebView2, необходимый для интерфейса этого приложения (вероятно, нет подключения к интернету). Подключитесь к интернету и перезапустите приложение, либо установите компонент вручную по адресу https://developer.microsoft.com/microsoft-edge/webview2/consumer/.",
    };
    private static readonly Dictionary<string, string> Es = new()
    {
        ["Göster"] = "Mostrar",
        ["Çıkış"] = "Salir",
        ["Program başlatılıyor…"] = "Iniciando el programa…",
        ["Veritabanı Onarılıyor"] = "Reparando la base de datos",
        ["Geçen seferki beklenmedik kapanmadan kalan bir sorun düzeltiliyor…"] = "Corrigiendo un problema derivado del último cierre inesperado…",
        ["{0} bozuk görünüyor, onarılıyor…"] = "{0} parece dañado, reparando…",
        ["Tamamlandı"] = "Completado",
        ["Bu uygulamanın arayüzü için gereken Microsoft Edge WebView2 bileşeni kurulamadı (muhtemelen internet bağlantısı yok). Lütfen internete bağlanıp uygulamayı tekrar başlatın, ya da https://developer.microsoft.com/microsoft-edge/webview2/consumer/ adresinden elle kurun."]
            = "No se pudo instalar el componente Microsoft Edge WebView2 necesario para la interfaz de esta aplicación (probablemente no hay conexión a internet). Conéctese a internet y reinicie la app, o instálelo manualmente desde https://developer.microsoft.com/microsoft-edge/webview2/consumer/.",
    };
    private static readonly Dictionary<string, string> Zh = new()
    {
        ["Göster"] = "显示",
        ["Çıkış"] = "退出",
        ["Program başlatılıyor…"] = "正在启动程序…",
        ["Veritabanı Onarılıyor"] = "正在修复数据库",
        ["Geçen seferki beklenmedik kapanmadan kalan bir sorun düzeltiliyor…"] = "正在修复上次意外关闭遗留的问题…",
        ["{0} bozuk görünüyor, onarılıyor…"] = "{0} 似乎已损坏，正在修复…",
        ["Tamamlandı"] = "已完成",
        ["Bu uygulamanın arayüzü için gereken Microsoft Edge WebView2 bileşeni kurulamadı (muhtemelen internet bağlantısı yok). Lütfen internete bağlanıp uygulamayı tekrar başlatın, ya da https://developer.microsoft.com/microsoft-edge/webview2/consumer/ adresinden elle kurun."]
            = "无法安装此应用界面所需的 Microsoft Edge WebView2 组件（可能是没有网络连接）。请连接网络后重启应用，或前往 https://developer.microsoft.com/microsoft-edge/webview2/consumer/ 手动安装。",
    };

    private static readonly Dictionary<string, string> Fr = new()
    {
        ["Göster"] = "Afficher",
        ["Çıkış"] = "Quitter",
        ["Program başlatılıyor…"] = "Démarrage du programme…",
        ["Veritabanı Onarılıyor"] = "Réparation de la base de données",
        ["Geçen seferki beklenmedik kapanmadan kalan bir sorun düzeltiliyor…"] = "Correction d'un problème laissé par le dernier arrêt inattendu…",
        ["{0} bozuk görünüyor, onarılıyor…"] = "{0} semble endommagé, réparation en cours…",
        ["Tamamlandı"] = "Terminé",
        ["Bu uygulamanın arayüzü için gereken Microsoft Edge WebView2 bileşeni kurulamadı (muhtemelen internet bağlantısı yok). Lütfen internete bağlanıp uygulamayı tekrar başlatın, ya da https://developer.microsoft.com/microsoft-edge/webview2/consumer/ adresinden elle kurun."]
            = "Le composant Microsoft Edge WebView2 requis pour l'interface de cette application n'a pas pu être installé (probablement en l'absence de connexion internet). Veuillez vous connecter à internet et redémarrer l'application, ou installez-le manuellement depuis https://developer.microsoft.com/microsoft-edge/webview2/consumer/.",
    };
    private static readonly Dictionary<string, string> Pt = new()
    {
        ["Göster"] = "Mostrar",
        ["Çıkış"] = "Sair",
        ["Program başlatılıyor…"] = "Iniciando o programa…",
        ["Veritabanı Onarılıyor"] = "Reparando o banco de dados",
        ["Geçen seferki beklenmedik kapanmadan kalan bir sorun düzeltiliyor…"] = "Corrigindo um problema deixado pelo último fechamento inesperado…",
        ["{0} bozuk görünüyor, onarılıyor…"] = "{0} parece estar corrompido, reparando…",
        ["Tamamlandı"] = "Concluído",
        ["Bu uygulamanın arayüzü için gereken Microsoft Edge WebView2 bileşeni kurulamadı (muhtemelen internet bağlantısı yok). Lütfen internete bağlanıp uygulamayı tekrar başlatın, ya da https://developer.microsoft.com/microsoft-edge/webview2/consumer/ adresinden elle kurun."]
            = "Não foi possível instalar o componente Microsoft Edge WebView2 necessário para a interface deste aplicativo (provavelmente sem conexão com a internet). Conecte-se à internet e reinicie o aplicativo, ou instale-o manualmente em https://developer.microsoft.com/microsoft-edge/webview2/consumer/.",
    };
    private static readonly Dictionary<string, string> Ja = new()
    {
        ["Göster"] = "表示",
        ["Çıkış"] = "終了",
        ["Program başlatılıyor…"] = "プログラムを起動しています…",
        ["Veritabanı Onarılıyor"] = "データベースを修復しています",
        ["Geçen seferki beklenmedik kapanmadan kalan bir sorun düzeltiliyor…"] = "前回の予期しない終了によって残った問題を修復しています…",
        ["{0} bozuk görünüyor, onarılıyor…"] = "{0} が破損しているようです。修復しています…",
        ["Tamamlandı"] = "完了",
        ["Bu uygulamanın arayüzü için gereken Microsoft Edge WebView2 bileşeni kurulamadı (muhtemelen internet bağlantısı yok). Lütfen internete bağlanıp uygulamayı tekrar başlatın, ya da https://developer.microsoft.com/microsoft-edge/webview2/consumer/ adresinden elle kurun."]
            = "このアプリのインターフェースに必要な Microsoft Edge WebView2 コンポーネントをインストールできませんでした（インターネット接続がない可能性があります）。インターネットに接続してアプリを再起動するか、https://developer.microsoft.com/microsoft-edge/webview2/consumer/ から手動でインストールしてください。",
    };

    private static readonly Dictionary<string, Dictionary<string, string>> Dicts = new()
    {
        [LanguageSettings.English] = En,
        [LanguageSettings.German] = De,
        [LanguageSettings.Russian] = Ru,
        [LanguageSettings.Spanish] = Es,
        [LanguageSettings.Chinese] = Zh,
        [LanguageSettings.French] = Fr,
        [LanguageSettings.Portuguese] = Pt,
        [LanguageSettings.Japanese] = Ja,
    };

    internal static string T(string turkish) =>
        Dicts.TryGetValue(LanguageSettings.GetLanguage(), out Dictionary<string, string>? dict) && dict.TryGetValue(turkish, out string? translated)
            ? translated
            : turkish;
}
