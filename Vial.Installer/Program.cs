using System;
using System.Windows.Forms;
using System.IO.Compression;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using dnlib.DotNet;
using Vial.Mixins;

namespace Vial.Installer
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            OpenFileDialog open = new OpenFileDialog
            {
                Title = "Select the Town of Salem APK",
                Filter = "Android Package|*.apk"
            };
            SaveFileDialog save = new SaveFileDialog
            {
                Title = "Save the patched APK as",
                Filter = "Android Package|*.apk"
            };
            if (open.ShowDialog() == DialogResult.OK && save.ShowDialog() == DialogResult.OK)
            {
                File.Delete(save.FileName);
                using (ZipArchive archive = new ZipArchive(open.OpenFile(), ZipArchiveMode.Read), patched = new ZipArchive(save.OpenFile(), ZipArchiveMode.Update))
                {
                    using (AssemblyList modules = new AssemblyList())
                    {
                        AssemblyPatcher patcher = new AssemblyPatcher();
                        patcher.Add(typeof(Placeholder).Assembly.ToPatch());
                        byte[] buffer = new byte[4096];
                        int read;
                        foreach (ZipArchiveEntry entry in archive.Entries)
                        {
                            if (entry.FullName.StartsWith("META-INF/") && (entry.FullName.EndsWith(".RSA") || entry.FullName.EndsWith("SF")))
                            {
                                Debug.WriteLine("Skipping  {0}", (object)entry.FullName);
                                continue;
                            }
                            ZipArchiveEntry copy = patched.CreateEntry(entry.FullName);
                            copy.LastWriteTime = entry.LastWriteTime;
                            if (entry.FullName.EndsWith(".dll"))
                            {
                                Debug.WriteLine("Patching  {0}", (object)entry.FullName);
                                modules.Add(entry.Open(), copy.Open(), entry.Length);
                                continue;
                            }
                            Debug.WriteLine("Copying   {0}", (object)entry.FullName);
                            using (Stream input = entry.Open(), output = copy.Open())
                            {
                                while ((read = input.Read(buffer, 0, buffer.Length)) > 0) output.Write(buffer, 0, read);
                            }
                        }
                        patcher.Patch(modules.Get);
                    }
                    Debug.WriteLine("Writing to APK");
                }
            }
        }

        private class AssemblyList : IDisposable
        {
            private readonly Dictionary<UTF8String, (MemoryStream input, Stream output, AssemblyDef assembly)> assemblies = new Dictionary<UTF8String, (MemoryStream input, Stream output, AssemblyDef assembly)>();
            private readonly Dictionary<UTF8String, ModuleDef> modules = new Dictionary<UTF8String, ModuleDef>();

            public AssemblyDef this[UTF8String name] => assemblies[name].assembly;

            public ModuleDef Get(UTF8String name) => modules[name + ".dll"];    // TODO Improve this to work with UTF8String

            public void Add(Stream input, Stream output, long length)
            {
                byte[] buffer = new byte[length];
                input.Read(buffer, 0, buffer.Length);
                input.Dispose();
                Add(new MemoryStream(buffer), output);
            }

            public void Add(MemoryStream input, Stream output)
            {
                AssemblyDef assembly = AssemblyDef.Load(input);
                assemblies.Add(assembly.Name, (input, output, assembly));
                foreach (ModuleDef module in assembly.Modules) modules.Add(module.Name, module);
            }

            public void Dispose()
            {
                foreach ((MemoryStream input, Stream output, AssemblyDef assembly) in assemblies.Values)
                {
                    input.Dispose();
                    assembly.Write(output);
                    output.Dispose();
                }
            }
        }
    }
}
