using System;
using System.Windows.Forms;
using System.IO.Compression;
using System.Diagnostics;
using System.IO;
using Mono.Cecil;
using System.Text;
using System.Linq;
using Mono.Cecil.Cil;
using System.Collections.Generic;
using Mono.Collections.Generic;
using System.Reflection;

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
                            using (Stream input = entry.Open(), output = copy.Open()) PatchDll(input, output, entry.Length);
                            continue;
                        }
                        if (entry.FullName == "assets/bin/Data/level0")
                        {
                            Debug.WriteLine("Patching  {0}", (object)entry.FullName);
                            using (Stream input = entry.Open(), output = copy.Open()) PatchScene(input, output, (int)entry.Length);
                            continue;
                        }
                        Debug.WriteLine("Copying   {0}", (object)entry.FullName);
                        using (Stream input = entry.Open(), output = copy.Open())
                        {
                            while ((read = input.Read(buffer, 0, buffer.Length)) > 0) output.Write(buffer, 0, read);
                        }
                    }
                    ZipArchiveEntry injected = patched.CreateEntry("assets/bin/Data/Managed/Vial.dll");
                    Debug.WriteLine("Injecting {0}", (object)injected.FullName);
                    AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(typeof(VialServiceLocator).Assembly.Location);
                    assembly.EntryPoint = null;
                    List<ModuleDefinition> cull = new List<ModuleDefinition>();
                    foreach (ModuleDefinition module in assembly.Modules)
                    {
                        if (!module.TryGetTypeReference(typeof(VialServiceLocator).FullName, out _))
                        {
                            cull.Add(module);
                            continue;
                        }
                        module.ModuleReferences.Clear();
                    }
                    foreach (ModuleDefinition module in cull) assembly.Modules.Remove(module);
                    using (Stream output = injected.Open()) assembly.Write(output);
                    Debug.WriteLine("Writing to APK");
                }
            }
        }

        private static void PatchDll(Stream input, Stream output, long length)
        {
            byte[] buffer = new byte[length];
            input.Read(buffer, 0, buffer.Length);
            using (MemoryStream wrapped = new MemoryStream(buffer))
            {
                AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(wrapped, new ReaderParameters() { AssemblyResolver = new VialAssemblyResolver() });
                AssemblyNameReference vial = new AssemblyNameReference("Vial", typeof(VialServiceLocator).Assembly.GetName().Version);
                assembly.MainModule.AssemblyReferences.Add(vial);
                IDictionary<TypeReference, TypeReference> wrapperTypes = new Dictionary<TypeReference, TypeReference>();
                IDictionary<MethodReference, MethodReference> wrappers = new Dictionary<MethodReference, MethodReference>();
                foreach (TypeInfo type in typeof(VialServiceLocator).Assembly.DefinedTypes.Where(ti => ti.GetCustomAttributes<WrapAttribute>().Any()))
                {
                    TypeDefinition wrappedType = assembly.MainModule.GetType(type.BaseType.FullName);
                    wrapperTypes.Add(wrappedType, assembly.MainModule.ImportReference(type));
                    foreach (ConstructorInfo ctor in type.DeclaredConstructors)
                    {
                        foreach (MethodDefinition method in wrappedType.Methods.Where(md => md.IsConstructor && CompareSignatures(md.Parameters, ctor.GetParameters())))
                        {
                            wrappers.Add(method, assembly.MainModule.ImportReference(ctor));
                        }
                    }
                }
                foreach (TypeDefinition type in assembly.MainModule.GetTypes())
                {
                    if (type.BaseType != null && wrapperTypes.TryGetValue(type.BaseType, out TypeReference typeSub)) type.BaseType = typeSub;
                    if (type.HasFields) foreach (FieldDefinition field in type.Fields) if (wrapperTypes.TryGetValue(field.FieldType, out typeSub)) field.FieldType = typeSub;
                    if (!type.HasMethods) continue;
                    foreach (MethodDefinition method in type.Methods.Where(md => md.HasBody))
                    {
                        ILProcessor il = method.Body.GetILProcessor();
                        IList<(Instruction a, Instruction b)> substitutions = new List<(Instruction a, Instruction b)>();
                        foreach (Instruction inst in method.Body.Instructions) if (inst.Operand is MethodReference mr && wrappers.TryGetValue(mr, out MethodReference methodSub)) substitutions.Add((inst, Instruction.Create(inst.OpCode, methodSub)));
                        foreach ((Instruction a, Instruction b) in substitutions) il.Replace(a, b);
                    }
                }
                TypeDefinition vsl = assembly.MainModule.AssemblyResolver.Resolve(vial).Modules.First(md => md.Types.Any(td => td.FullName == typeof(VialServiceLocator).FullName)).GetType(typeof(VialServiceLocator).FullName);
                TypeDefinition wsl = new TypeDefinition("Vial", "GenServiceLocator", vsl.Attributes);
                assembly.MainModule.Types.Add(wsl);
                wsl.BaseType = assembly.MainModule.ImportReference(vsl);
                foreach (MethodDefinition ctor in vsl.Methods.Where(md => md.IsConstructor && !md.IsPrivate))
                {
                    MethodDefinition ctorCopy = new MethodDefinition(ctor.Name, ctor.Attributes, assembly.MainModule.TypeSystem.Void);
                    ctorCopy.Parameters.Add(new ParameterDefinition(ctor.Parameters[0].Name, ctor.Parameters[0].Attributes, assembly.MainModule.ImportReference(ctor.Parameters[0].ParameterType)));
                    ctorCopy.Parameters.Add(new ParameterDefinition(ctor.Parameters[1].Name, ctor.Parameters[1].Attributes, assembly.MainModule.ImportReference(ctor.Parameters[1].ParameterType)));
                    ctorCopy.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
                    if (ctor.Parameters.Count > 0) ctorCopy.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
                    if (ctor.Parameters.Count > 1) ctorCopy.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_2));
                    if (ctor.Parameters.Count > 2) ctorCopy.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_3));
                    for (int argidx = 4; argidx <= ctor.Parameters.Count; argidx++) ctorCopy.Body.Instructions.Add(Instruction.Create(argidx <= byte.MaxValue ? OpCodes.Ldarg_S : OpCodes.Ldarg, argidx));
                    ctorCopy.Body.Instructions.Add(Instruction.Create(OpCodes.Call, assembly.MainModule.ImportReference(ctor)));
                    ctorCopy.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
                    wsl.Methods.Add(ctorCopy);
                }
                assembly.Write(output);
            }
        }

        private static void PatchScene(Stream input, Stream output, int size)
        {
            byte[] repSeq = Encoding.UTF8.GetBytes("StandardServiceLocator, Assembly-CSharp");
            byte[] replacement = Encoding.UTF8.GetBytes("Vial.GenServiceLocator, Assembly-CSharp");
            byte[] buffer = new byte[size];
            input.Read(buffer, 0, size);
            for (int start = 0; start <= buffer.Length - repSeq.Length; start++)
            {
                if (Enumerable.SequenceEqual(buffer.Skip(start).Take(repSeq.Length), repSeq))
                {
                    output.Write(buffer, 0, start);
                    output.Write(replacement, 0, replacement.Length);
                    output.Write(buffer, start + repSeq.Length, size - start - repSeq.Length);
                    return;
                }
            }
        }

        private static bool CompareSignatures(Collection<ParameterDefinition> a, ParameterInfo[] b)
        {
            foreach ((ParameterDefinition pa, ParameterInfo pb) in a.Zip(b, (pa, pb) => (pa, pb)))
            {
                if (pa.ParameterType.FullName != pb.ParameterType.FullName || pa.IsOut != pb.IsOut || pa.IsIn != pb.IsIn || pa.HasDefault != pb.HasDefaultValue || pa.IsOptional != pb.IsOptional) return false;
            }
            return true;
        }

        private class VialAssemblyResolver : IAssemblyResolver
        {
            private DefaultAssemblyResolver defaultResolver;
            private ISet<MemoryStream> streams = new HashSet<MemoryStream>();

            public VialAssemblyResolver() => defaultResolver = new DefaultAssemblyResolver();

            public AssemblyDefinition Resolve(AssemblyNameReference name) => Resolve(name, new ReaderParameters());

            public AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters)
            {
                try
                {
                    return defaultResolver.Resolve(name, parameters);
                }
                catch (AssemblyResolutionException)
                {
                    string candidate = AppDomain.CurrentDomain.Load(new AssemblyName(name.FullName)).Location;
                    if (candidate == "") candidate = Assembly.GetEntryAssembly().Location;
                    return AssemblyDefinition.ReadAssembly(candidate);
                }
            }

            public void Dispose()
            {
                try
                {
                    defaultResolver.Dispose();
                }
                finally
                {
                    foreach (Stream stream in streams)
                    {
                        try
                        {
                            stream.Dispose();
                        }
                        catch { }
                    }
                }
            }
        }
    }
}
