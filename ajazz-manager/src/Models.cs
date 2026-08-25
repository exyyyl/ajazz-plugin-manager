using System;

namespace AjazzManager
{
    internal enum CompatibilityLevel
    {
        Supported,
        Experimental,
        Protected,
        Unsupported,
        InstalledOnly
    }

    internal sealed class PluginEntry
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Version { get; set; }
        public string InstalledVersion { get; set; }
        public string SourcePath { get; set; }
        public string SourceLabel { get; set; }
        public string CodePath { get; set; }
        public int ActionCount { get; set; }
        public bool IsInstalled { get; set; }
        public bool IsPackaged { get; set; }
        public bool IsIconEditor { get; set; }
        public bool IsProtected { get; set; }
        public CompatibilityLevel Compatibility { get; set; }

        public string CompatibilityText
        {
            get
            {
                if (Compatibility == CompatibilityLevel.Supported) return "Проверен";
                if (Compatibility == CompatibilityLevel.Experimental) return "Экспериментальный";
                if (Compatibility == CompatibilityLevel.Protected) return "Защищён Elgato";
                if (Compatibility == CompatibilityLevel.Unsupported) return "Не поддерживается";
                return "Только установлен";
            }
        }

        public string InstalledText
        {
            get { return IsInstalled ? "Да — " + InstalledVersion : "Нет"; }
        }
    }

    internal sealed class BackupEntry
    {
        public string PluginId { get; set; }
        public string PluginName { get; set; }
        public string Version { get; set; }
        public string Path { get; set; }
        public DateTime Created { get; set; }
    }

    internal sealed class IconEntry
    {
        public string Path { get; set; }
        public string Name { get; set; }
        public string PackName { get; set; }
        public string Source { get; set; }
        public string Extension { get; set; }
        public long Size { get; set; }
    }

    internal sealed class ManifestSummary
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Version { get; set; }
        public string CodePath { get; set; }
        public int ActionCount { get; set; }
        public bool HasIconEditor { get; set; }
        public bool IsProtected { get; set; }
    }
}
