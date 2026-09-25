using System.Reflection;
using System.Runtime.Loader;
using System.Text;

// noirelangtemplate <plugin.dll> <dalamud folder> <template.lang> [language files...]
if (args.Length < 3)
{
    Console.Error.WriteLine("usage: noirelangtemplate <plugin.dll> <dalamud folder> <template.lang> [language files...]");
    return 2;
}

var pluginPath = Path.GetFullPath(args[0]);
var context = new PluginLoadContext(Path.GetDirectoryName(pluginPath)!, args[1]);
var plugin = context.LoadFromAssemblyPath(pluginPath);
var template = context.LoadFromAssemblyName(new AssemblyName("NoireLib")).GetType("NoireLib.Localizer.NoireLanguageTemplate", throwOnError: true)!;
var build = template.GetMethod("Build")!;
var update = template.GetMethod("Update")!;
var problems = new List<string>();

Write(args[2], (string)build.Invoke(null, [plugin, problems])!);

foreach (var file in args.Skip(3))
{
    var language = Path.GetFileNameWithoutExtension(file);
    Write(file, (string)update.Invoke(null, [plugin, language, File.ReadAllText(file), problems])!);
}

foreach (var problem in problems.Distinct())
    Console.WriteLine("warning NOIRELANG001: " + problem);

return 0;

static void Write(string path, string text)
{
    var full = Path.GetFullPath(path);

    if (File.Exists(full))
    {
        var current = File.ReadAllText(full);

        if (current.Contains("\r\n"))
            text = text.Replace("\n", "\r\n");

        if (current == text)
            return;
    }

    Directory.CreateDirectory(Path.GetDirectoryName(full)!);
    File.WriteAllText(full, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    Console.WriteLine("noirelangtemplate: wrote " + full);
}

sealed class PluginLoadContext(string pluginFolder, string dalamudFolder) : AssemblyLoadContext("noirelangtemplate")
{
    protected override Assembly? Load(AssemblyName name)
    {
        foreach (var folder in new[] { pluginFolder, dalamudFolder })
        {
            var path = Path.Combine(folder, name.Name + ".dll");

            if (File.Exists(path))
                return LoadFromAssemblyPath(path);
        }

        return null;
    }
}
