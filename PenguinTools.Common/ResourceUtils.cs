﻿using System.Reflection;

namespace PenguinTools.Common;

public static class ResourceUtils
{
    private const string Name = "PenguinTools.Temp";
    private static readonly Lock Lock = new();
    private static bool _isInitialized;
    public static string TempWorkPath => Path.Combine(Path.GetTempPath(), Name);

    public static void Initialize()
    {
        lock (Lock)
        {
            if (_isInitialized) return;
            Directory.CreateDirectory(TempWorkPath);
            var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            if (!path.Contains(TempWorkPath, StringComparison.OrdinalIgnoreCase))
            {
                Environment.SetEnvironmentVariable("PATH", $"{TempWorkPath};{path}");
            }
            _isInitialized = true;
        }
    }

    public static string GetTempPath(string fileName)
    {
        Initialize();
        if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentNullException(nameof(fileName));
        var tempPath = Path.Combine(TempWorkPath, fileName);
        return tempPath;
    }

    #region Storage

    public static void Release()
    {
        lock (Lock)
        {
            foreach (var filePath in Directory.GetFiles(TempWorkPath))
            {
                try
                {
                    File.Delete(filePath);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                }
            }
            try
            {
                Directory.Delete(TempWorkPath, true);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }
    }

    public static void Save(string fileName, Stream resource)
    {
        Initialize();

        if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentNullException(nameof(fileName));
        if (resource == null || resource.Length == 0) throw new ArgumentNullException(nameof(resource));

        lock (Lock)
        {
            var finalPath = Path.Combine(TempWorkPath, fileName);
            using var fileStream = File.Create(finalPath);
            resource.CopyTo(fileStream);
        }
    }

    #endregion

    #region Resources

    public static Stream GetStream(string resourceName)
    {
        var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName) ?? throw new FileNotFoundException($"Resource '{resourceName}' not found in assembly '{Name}'.");
        return stream;
    }

    public static byte[] GetByte(string resourceName)
    {
        var stream = GetStream(resourceName);
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    #endregion"
}