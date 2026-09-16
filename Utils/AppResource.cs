using System.Drawing;
using System.Reflection;
namespace MuSync.Utils;
/// <summary>内嵌资源访问：程序图标（读取失败时回退系统默认图标）。</summary>
internal static class AppResource
{
    public static Icon Icon { get; }
    static AppResource()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            const string resourceName = "MuSync.Resources.icon.ico";
            using var stream = assembly.GetManifestResourceStream(resourceName);
            Icon = stream != null ? new Icon(stream) : SystemIcons.Application;
        }
        catch
        {
            Icon = SystemIcons.Application;
        }
    }
}