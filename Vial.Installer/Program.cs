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
                        if (entry.FullName == "assets/bin/Data/Managed/Assembly-CSharp.dll")
                        {
                            Debug.WriteLine("Patching  {0}", (object)entry.FullName);
                            using (Stream input = entry.Open(), output = copy.Open()) PatchDll(input, output, entry.Length, typeof(MixinAttribute).Assembly);
                            continue;
                        }
                        Debug.WriteLine("Copying   {0}", (object)entry.FullName);
                        using (Stream input = entry.Open(), output = copy.Open())
                        {
                            while ((read = input.Read(buffer, 0, buffer.Length)) > 0) output.Write(buffer, 0, read);
                        }
                    }
                    Debug.WriteLine("Writing to APK");
                }
            }
        }

        private static void PatchDll(Stream input, Stream output, long length, params Assembly[] patches)
        {
            byte[] buffer = new byte[length];
            input.Read(buffer, 0, buffer.Length);
            using (MemoryStream wrapped = new MemoryStream(buffer))
            {
                AssemblyDef assembly = AssemblyDef.Load(wrapped);
                AssemblyPatcher patcher = new AssemblyPatcher();
                new ReflectionLoader(patcher).Add(typeof(Placeholder).Assembly);
                foreach (ModuleDef module in assembly.Modules) patcher.Patch(module);
                assembly.Write(output);
            }
        }
    }
}
