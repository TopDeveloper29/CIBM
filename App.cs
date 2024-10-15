using System;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;

namespace CIBM
{
    internal class App
    {
        [STAThread]
        public static void Main(string[] Args)
        {
            AppDomain.CurrentDomain.AssemblyResolve += OnResolveAssembly;
            Program.Main(Args);
        }
        private static Assembly OnResolveAssembly(object sender, ResolveEventArgs args)
        {
            var executingAssembly = Assembly.GetExecutingAssembly();
            var assemblyName = new AssemblyName(args.Name);

            var path = assemblyName.Name + ".dll";
            if (!assemblyName.CultureInfo.Equals(CultureInfo.InvariantCulture))
            {
                path = $"{assemblyName.CultureInfo}\\${path}";
            }

            var stream = executingAssembly.GetManifestResourceStream(path);
            if (stream == null)
                return null;

            var assemblyRawBytes = new byte[stream.Length];
            stream.Read(assemblyRawBytes, 0, assemblyRawBytes.Length);
            return Assembly.Load(assemblyRawBytes);
        }

    }
}
