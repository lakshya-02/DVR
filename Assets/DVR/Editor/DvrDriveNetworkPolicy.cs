using System.IO;
using System.Xml.Linq;
using UnityEditor.Android;

public sealed class DvrDriveNetworkPolicy : IPostGenerateGradleAndroidProject
{
    public int callbackOrder => 0;

    public void OnPostGenerateGradleAndroidProject(string path)
    {
        var debug = Path.Combine(path, "..", "launcher", "src", "debug");
        var xml = Path.Combine(debug, "res", "xml");
        Directory.CreateDirectory(xml);
        new XDocument(new XElement("network-security-config",
            new XElement("base-config", new XAttribute("cleartextTrafficPermitted", "false")),
            new XElement("domain-config", new XAttribute("cleartextTrafficPermitted", "true"),
                new XElement("domain", "127.0.0.1")))).Save(Path.Combine(xml, "dvr_wired_pilot.xml"));

        var manifestPath = Path.Combine(debug, "AndroidManifest.xml");
        XNamespace android = "http://schemas.android.com/apk/res/android";
        var manifest = File.Exists(manifestPath) ? XDocument.Load(manifestPath) : new XDocument(new XElement("manifest",
            new XAttribute(XNamespace.Xmlns + "android", android.NamespaceName)));
        var application = manifest.Root.Element("application");
        if (application == null)
        {
            application = new XElement("application");
            manifest.Root.Add(application);
        }
        XNamespace tools = "http://schemas.android.com/tools";
        manifest.Root.SetAttributeValue(XNamespace.Xmlns + "tools", tools.NamespaceName);
        var replace = (string)application.Attribute(tools + "replace");
        if (string.IsNullOrEmpty(replace)) replace = "android:networkSecurityConfig";
        else if (System.Array.IndexOf(replace.Replace(" ", "").Split(','), "android:networkSecurityConfig") < 0)
            replace += ",android:networkSecurityConfig";
        application.SetAttributeValue(tools + "replace", replace);
        application.SetAttributeValue(android + "networkSecurityConfig", "@xml/dvr_wired_pilot");
        manifest.Save(manifestPath);
    }
}
