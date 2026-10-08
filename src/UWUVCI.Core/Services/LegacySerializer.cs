using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text.RegularExpressions;
using GameBaseClassLibrary;
using UWUVCI_AIO_WPF;
using UWUVCI_AIO_WPF.Models;

namespace UWUVCI.Core.Services
{
    /// <summary>
    /// Reads and writes the gzip + BinaryFormatter files of the Windows app (.vcb base lists, .vck key files,
    /// .uwuvci configs) so they stay interchangeable between the two apps.
    /// </summary>
    public static class LegacySerializer
    {
        private const string WindowsAssembly = "UWUVCI AIO WPF, Version=3.0.0.0, Culture=neutral, PublicKeyToken=null";

        public static T Read<T>(string path)
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read);
            using var gzip = new GZipStream(file, CompressionMode.Decompress);
            var formatter = new BinaryFormatter { Binder = new Binder() };
            return (T)formatter.Deserialize(gzip);
        }

        public static void Write(string path, object value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            using var file = new FileStream(path, FileMode.Create, FileAccess.Write);
            using var gzip = new GZipStream(file, CompressionMode.Compress);
            var formatter = new BinaryFormatter { Binder = new Binder() };
            formatter.Serialize(gzip, value);
        }

        /// <summary>
        /// Maps the Windows app's assembly name onto this one, and refuses every type these files have no business containing.
        /// </summary>
        private sealed class Binder : SerializationBinder
        {
            private static readonly HashSet<Type> Allowed = new HashSet<Type>
            {
                typeof(GameConfig), typeof(PNGTGA), typeof(N64Conf), typeof(TKeys),
                typeof(GameBases), typeof(Regions), typeof(GameConsoles),
                typeof(List<TKeys>), typeof(List<GameBases>), typeof(TKeys[]), typeof(GameBases[]),
                typeof(byte[]), typeof(string), typeof(bool), typeof(int),
            };

            private static readonly string Mine = typeof(GameConfig).Assembly.FullName;
            private static readonly Regex WindowsAssemblyRef = new Regex(@"UWUVCI AIO WPF, Version=[^,\]]+, Culture=[^,\]]+, PublicKeyToken=[^,\]]+");

            public override Type BindToType(string assemblyName, string typeName)
            {
                typeName = WindowsAssemblyRef.Replace(typeName, Mine);
                if (assemblyName.StartsWith("UWUVCI AIO WPF", StringComparison.Ordinal))
                    assemblyName = Mine;
                else if (assemblyName.StartsWith("mscorlib", StringComparison.Ordinal))
                    assemblyName = typeof(object).Assembly.FullName;

                Type type = Type.GetType($"{typeName}, {assemblyName}", throwOnError: false);
                if (type == null || !Allowed.Contains(type))
                    throw new SerializationException($"Unexpected type in file: {typeName}");
                return type;
            }

            public override void BindToName(Type serializedType, out string assemblyName, out string typeName)
            {
                assemblyName = null;
                typeName = null;
                if (serializedType.Assembly == typeof(GameConfig).Assembly)
                    assemblyName = WindowsAssembly;
                if (serializedType.FullName.Contains(Mine))
                {
                    typeName = serializedType.FullName.Replace(Mine, WindowsAssembly);
                    // List<T> itself lives in mscorlib as far as the .NET Framework reader is concerned.
                    assemblyName = "mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089";
                }
            }
        }
    }
}
