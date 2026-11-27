using IcoConverter.Models;
using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace IcoConverter.Services
{
    public sealed class ExecutableIconReader
    {
        public async Task<IReadOnlyList<ExecutableIconCandidate>> ScanExecutableIconsAsync(string binaryPath, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrEmpty(binaryPath);

            if (!File.Exists(binaryPath))
            {
                throw new FileNotFoundException("未找到可执行文件", binaryPath);
            }

            // 先扫描输入文件，再检查卫星资源，使用首个包含图标的文件。
            var probePaths = BuildResourceProbeList(binaryPath);
            Exception? lastError = null;

            foreach (var path in probePaths)
            {
                try
                {
                    var icons = await Task.Run(() => ExtractIconsFromFile(path, cancellationToken), cancellationToken).ConfigureAwait(false);
                    if (icons.Count > 0)
                    {
                        return icons;
                    }

                    lastError = new InvalidOperationException($"在文件 '{path}' 中未找到图标资源。");
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }
            }

            throw lastError ?? new InvalidOperationException("未在指定文件及其资源包中找到任何图标。");
        }

        private static IReadOnlyList<string> BuildResourceProbeList(string binaryPath)
        {
            var results = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void TryAdd(string? candidate)
            {
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    return;
                }

                if (!File.Exists(candidate))
                {
                    return;
                }

                if (seen.Add(candidate))
                {
                    results.Add(candidate);
                }
            }

            TryAdd(binaryPath);

            var directory = Path.GetDirectoryName(binaryPath);
            var fileName = Path.GetFileName(binaryPath);
            if (string.IsNullOrEmpty(fileName))
            {
                return results;
            }

            // System32 与 SystemResources 目录下存在大量 exe 对应的 .mui 资源文件，需一并检索
            var probeRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(directory))
            {
                probeRoots.Add(directory);
            }

            var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (!string.IsNullOrEmpty(windowsDir))
            {
                probeRoots.Add(windowsDir);
                probeRoots.Add(Path.Combine(windowsDir, "System32"));
                probeRoots.Add(Path.Combine(windowsDir, "SystemResources"));
            }

            var systemDir = Environment.SystemDirectory;
            if (!string.IsNullOrEmpty(systemDir))
            {
                probeRoots.Add(systemDir);
            }

            foreach (var root in probeRoots.Where(Directory.Exists))
            {
                AddSatelliteCandidates(root, fileName, TryAdd);
            }

            return results;
        }

        private static void AddSatelliteCandidates(string rootDirectory, string fileName, Action<string?> tryAdd)
        {
            if (string.IsNullOrEmpty(rootDirectory) || string.IsNullOrEmpty(fileName))
            {
                return;
            }

            tryAdd(Path.Combine(rootDirectory, fileName + ".mui"));
            tryAdd(Path.Combine(rootDirectory, fileName + ".mun"));

            // 形如 foo.exe.zh-CN.mui / foo.exe.2052.mui
            foreach (var file in SafeEnumerateFiles(rootDirectory, fileName + ".*.mui"))
            {
                tryAdd(file);
            }

            foreach (var file in SafeEnumerateFiles(rootDirectory, fileName + ".*.mun"))
            {
                tryAdd(file);
            }

            // 形如 zh-CN\foo.exe.mui
            foreach (var satelliteDir in SafeEnumerateDirectories(rootDirectory))
            {
                tryAdd(Path.Combine(satelliteDir, fileName + ".mui"));
                tryAdd(Path.Combine(satelliteDir, fileName + ".mun"));
            }
        }

        // 权限不足、路径过长等情况会抛出异常，遍历目录时统一按空结果处理
        private static IEnumerable<string> SafeEnumerateDirectories(string root)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            {
                yield break;
            }

            IEnumerable<string> directories;
            try
            {
                directories = Directory.EnumerateDirectories(root);
            }
            catch
            {
                yield break;
            }

            foreach (var directory in directories)
            {
                yield return directory;
            }
        }

        private static IEnumerable<string> SafeEnumerateFiles(string root, string searchPattern)
        {
            if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(searchPattern) || !Directory.Exists(root))
            {
                yield break;
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(root, searchPattern, SearchOption.TopDirectoryOnly);
            }
            catch
            {
                yield break;
            }

            foreach (var file in files)
            {
                yield return file;
            }
        }
        private static IReadOnlyList<ExecutableIconCandidate> ExtractIconsFromFile(string path, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Win32IconResourceEnumerator.Extract(path, cancellationToken);
        }

        // 仅实现枚举 RT_GROUP_ICON 的路径：组资源中包含各尺寸对应的 RT_ICON ID
        private sealed class Win32IconResourceEnumerator
        {
            private const int RtIcon = 3;
            private const int RtGroupIcon = 14;
            private const int ErrorResourceTypeNotFound = 1813;
            private const int ErrorResourceLangNotFound = 1815;

            private readonly SafeLibraryHandle _libraryHandle;
            private readonly IntPtr _moduleHandle;
            private readonly CancellationToken _cancellationToken;
            private readonly List<GroupResourceRecord> _groupRecords = new();
            private readonly Dictionary<(ushort Lang, ushort Id), byte[]> _iconCache = new();

            private Win32IconResourceEnumerator(SafeLibraryHandle libraryHandle, CancellationToken cancellationToken)
            {
                _libraryHandle = libraryHandle;
                _moduleHandle = libraryHandle.DangerousGetHandle();
                _cancellationToken = cancellationToken;
            }

            internal static IReadOnlyList<ExecutableIconCandidate> Extract(string path, CancellationToken cancellationToken)
            {
                var flags = NativeMethods.LoadLibraryFlags.LOAD_LIBRARY_AS_DATAFILE | NativeMethods.LoadLibraryFlags.LOAD_LIBRARY_AS_IMAGE_RESOURCE;
                var handle = NativeMethods.LoadLibraryEx(path, IntPtr.Zero, flags);
                if (handle.IsInvalid)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), $"无法加载文件 '{path}' 以扫描图标资源。");
                }

                using (handle)
                {
                    var enumerator = new Win32IconResourceEnumerator(handle, cancellationToken);
                    enumerator.EnumerateGroupResources();
                    return enumerator.BuildCandidates();
                }
            }

            private void EnumerateGroupResources()
            {
                var gcHandle = GCHandle.Alloc(this);
                try
                {
                    if (!NativeMethods.EnumResourceNames(_moduleHandle, new IntPtr(RtGroupIcon), EnumResourceNameThunk, GCHandle.ToIntPtr(gcHandle)))
                    {
                        var error = Marshal.GetLastWin32Error();
                        if (error != ErrorResourceTypeNotFound)
                        {
                            throw new Win32Exception(error, "枚举图标组资源失败。");
                        }
                    }
                }
                finally
                {
                    gcHandle.Free();
                }
            }

            private static bool EnumResourceNameThunk(IntPtr hModule, IntPtr type, IntPtr name, IntPtr parameter)
            {
                var handle = GCHandle.FromIntPtr(parameter);
                if (handle.Target is Win32IconResourceEnumerator enumerator)
                {
                    return enumerator.HandleGroupResource(type, name);
                }

                return false;
            }

            private bool HandleGroupResource(IntPtr type, IntPtr name)
            {
                _cancellationToken.ThrowIfCancellationRequested();

                var identifier = ResourceIdentifier.FromWin32Value(name);
                var context = new GroupLanguageContext(this, identifier);
                var gcHandle = GCHandle.Alloc(context);

                try
                {
                    if (!NativeMethods.EnumResourceLanguages(_moduleHandle, type, name, EnumResourceLanguageThunk, GCHandle.ToIntPtr(gcHandle)))
                    {
                        var error = Marshal.GetLastWin32Error();
                        if (error != ErrorResourceLangNotFound)
                        {
                            throw new Win32Exception(error, $"枚举资源语言失败: {identifier.ToDisplayString()}");
                        }
                    }
                }
                finally
                {
                    gcHandle.Free();
                }

                return true;
            }

            private static bool EnumResourceLanguageThunk(IntPtr hModule, IntPtr type, IntPtr name, ushort language, IntPtr parameter)
            {
                var handle = GCHandle.FromIntPtr(parameter);
                if (handle.Target is GroupLanguageContext context)
                {
                    return context.Owner.HandleGroupLanguage(context.Identifier, type, name, language);
                }

                return false;
            }

            private bool HandleGroupLanguage(ResourceIdentifier identifier, IntPtr type, IntPtr name, ushort language)
            {
                _cancellationToken.ThrowIfCancellationRequested();

                var data = ReadResourceBytes(type, name, language);
                if (data.Length == 0)
                {
                    return true;
                }

                _groupRecords.Add(new GroupResourceRecord(identifier, language, data));
                return true;
            }

            private byte[] ReadResourceBytes(IntPtr type, IntPtr name, ushort language)
            {
                var resourceHandle = NativeMethods.FindResourceEx(_moduleHandle, type, name, language);
                if (resourceHandle == IntPtr.Zero)
                {
                    return Array.Empty<byte>();
                }

                var size = NativeMethods.SizeofResource(_moduleHandle, resourceHandle);
                if (size == 0)
                {
                    return Array.Empty<byte>();
                }

                if (size > int.MaxValue)
                {
                    throw new InvalidOperationException("资源数据过大，无法加载。");
                }

                var dataHandle = NativeMethods.LoadResource(_moduleHandle, resourceHandle);
                if (dataHandle == IntPtr.Zero)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "加载资源失败。");
                }

                var pointer = NativeMethods.LockResource(dataHandle);
                if (pointer == IntPtr.Zero)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "锁定资源失败。");
                }

                var buffer = new byte[size];
                Marshal.Copy(pointer, buffer, 0, (int)size);
                return buffer;
            }

            private IReadOnlyList<ExecutableIconCandidate> BuildCandidates()
            {
                if (_groupRecords.Count == 0)
                {
                    return Array.Empty<ExecutableIconCandidate>();
                }

                var candidates = new List<ExecutableIconCandidate>(_groupRecords.Count);

                foreach (var group in _groupRecords)
                {
                    _cancellationToken.ThrowIfCancellationRequested();

                    var entries = IconResourceExtractor.ReadGroupEntries(group.Data);
                    if (entries.Count == 0)
                    {
                        continue;
                    }

                    var frames = entries
                        .Select(e => new IconFrameInfo(
                            IconResourceExtractor.NormalizeDimension(e.Width),
                            IconResourceExtractor.NormalizeDimension(e.Height),
                            e.BitCount))
                        .ToArray();

                    var iconPayloads = new List<byte[]>(entries.Count);
                    var missing = false;

                    foreach (var entry in entries)
                    {
                        if (!TryLoadIconEntry(entry.ResourceId, group.LanguageId, out var payload))
                        {
                            missing = true;
                            break;
                        }

                        iconPayloads.Add(payload);
                    }

                    if (missing)
                    {
                        continue;
                    }

                    var icoBytes = IconResourceExtractor.BuildIcoFromEntries(entries, iconPayloads);
                    var score = IconResourceExtractor.CalculateQualityScore(entries);
                    var groupId = group.NameIdentifier.TryGetId(out var id) ? id : -1;

                    candidates.Add(new ExecutableIconCandidate(
                        group.NameIdentifier.ToDisplayString(),
                        groupId,
                        group.LanguageId,
                        frames,
                        icoBytes,
                        score));
                }

                return candidates
                    .OrderByDescending(c => c.QualityScore)
                    .ThenByDescending(c => c.Frames.Max(f => f.Width * f.Height))
                    .ToArray();
            }

            private bool TryLoadIconEntry(ushort iconId, ushort preferredLanguage, out byte[] data)
            {
                if (TryGetCached(preferredLanguage, iconId, out data))
                {
                    return true;
                }

                if (TryReadIconBytes(iconId, preferredLanguage, out data))
                {
                    _iconCache[(preferredLanguage, iconId)] = data;
                    return true;
                }

                // 语言不匹配时回退到中立语言，仍不匹配则遍历该 ID 下的全部语言
                if (preferredLanguage != 0 && TryReadIconBytes(iconId, 0, out data))
                {
                    _iconCache[(0, iconId)] = data;
                    return true;
                }

                foreach (var language in EnumerateIconLanguages(iconId))
                {
                    if (language == preferredLanguage)
                    {
                        continue;
                    }

                    if (TryReadIconBytes(iconId, language, out data))
                    {
                        _iconCache[(language, iconId)] = data;
                        return true;
                    }
                }

                data = Array.Empty<byte>();
                return false;
            }

            private bool TryGetCached(ushort language, ushort iconId, out byte[] data)
            {
                return _iconCache.TryGetValue((language, iconId), out data!);
            }

            private bool TryReadIconBytes(ushort iconId, ushort language, out byte[] data)
            {
                var bytes = ReadResourceBytes(new IntPtr(RtIcon), new IntPtr(iconId), language);
                if (bytes.Length == 0)
                {
                    data = Array.Empty<byte>();
                    return false;
                }

                data = bytes;
                return true;
            }

            private IEnumerable<ushort> EnumerateIconLanguages(ushort iconId)
            {
                var languages = new List<ushort>();
                var gcHandle = GCHandle.Alloc(languages);
                try
                {
                    NativeMethods.EnumResourceLanguages(_moduleHandle, new IntPtr(RtIcon), new IntPtr(iconId), IconLanguageEnumThunk, GCHandle.ToIntPtr(gcHandle));
                }
                finally
                {
                    gcHandle.Free();
                }

                return languages;
            }

            private static bool IconLanguageEnumThunk(IntPtr hModule, IntPtr type, IntPtr name, ushort language, IntPtr parameter)
            {
                var handle = GCHandle.FromIntPtr(parameter);
                if (handle.Target is List<ushort> languages && !languages.Contains(language))
                {
                    languages.Add(language);
                }

                return true;
            }

            private sealed class GroupLanguageContext
            {
                internal GroupLanguageContext(Win32IconResourceEnumerator owner, ResourceIdentifier identifier)
                {
                    Owner = owner;
                    Identifier = identifier;
                }

                internal Win32IconResourceEnumerator Owner { get; }
                internal ResourceIdentifier Identifier { get; }
            }
        }

        private readonly struct GroupResourceRecord
        {
            internal GroupResourceRecord(ResourceIdentifier nameIdentifier, ushort languageId, byte[] data)
            {
                NameIdentifier = nameIdentifier;
                LanguageId = languageId;
                Data = data;
            }

            internal ResourceIdentifier NameIdentifier { get; }
            internal ushort LanguageId { get; }
            internal byte[] Data { get; }
        }

        private readonly struct ResourceIdentifier
        {
            private ResourceIdentifier(int id)
            {
                Id = id;
                Name = null;
            }

            private ResourceIdentifier(string name)
            {
                Name = name;
                Id = null;
            }

            internal int? Id { get; }
            internal string? Name { get; }

            internal static ResourceIdentifier FromId(int id) => new(id);
            internal static ResourceIdentifier FromName(string name) => new(name);
            internal static ResourceIdentifier FromWin32Value(IntPtr value)
            {
                if (IsIntResource(value))
                {
                    return FromId(value.ToInt32());
                }

                var text = Marshal.PtrToStringUni(value) ?? string.Empty;
                return FromName(text);
            }

            private static bool IsIntResource(IntPtr value) => ((ulong)value.ToInt64() >> 16) == 0;

            internal bool TryGetId(out int value)
            {
                if (Id.HasValue)
                {
                    value = Id.Value;
                    return true;
                }

                value = default;
                return false;
            }

            internal string ToDisplayString()
            {
                if (!string.IsNullOrWhiteSpace(Name))
                {
                    return Name!;
                }

                if (Id.HasValue)
                {
                    return Id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                return "未命名";
            }
        }

        private static class IconResourceExtractor
        {
            internal static IReadOnlyList<GroupIconEntry> ReadGroupEntries(byte[] groupBytes)
            {
                if (groupBytes == null || groupBytes.Length < 6)
                {
                    return Array.Empty<GroupIconEntry>();
                }

                using var ms = new MemoryStream(groupBytes, writable: false);
                using var reader = new BinaryReader(ms);

                var reserved = reader.ReadUInt16();
                var type = reader.ReadUInt16();
                var count = reader.ReadUInt16();

                if (reserved != 0 || type != 1)
                {
                    throw new InvalidOperationException("无效的图标组资源。");
                }

                var entries = new List<GroupIconEntry>(count);
                for (int i = 0; i < count; i++)
                {
                    entries.Add(new GroupIconEntry(
                        reader.ReadByte(),
                        reader.ReadByte(),
                        reader.ReadByte(),
                        reader.ReadByte(),
                        reader.ReadUInt16(),
                        reader.ReadUInt16(),
                        reader.ReadUInt32(),
                        reader.ReadUInt16()));
                }

                return entries
                    .OrderByDescending(e => NormalizeDimension(e.Width) * NormalizeDimension(e.Height))
                    .ThenByDescending(e => e.BitCount)
                    .ToArray();
            }

            internal static byte[] BuildIcoFromEntries(IReadOnlyList<GroupIconEntry> entries, IReadOnlyList<byte[]> iconData)
            {
                if (entries == null || entries.Count == 0)
                {
                    throw new InvalidOperationException("图标组为空。");
                }

                if (iconData == null || iconData.Count != entries.Count)
                {
                    throw new InvalidOperationException("图标数据与目录项数量不匹配。");
                }

                using var ms = new MemoryStream();
                using var writer = new BinaryWriter(ms);

                writer.Write((ushort)0);
                writer.Write((ushort)1);
                writer.Write((ushort)entries.Count);

                int offset = 6 + entries.Count * 16;
                for (int i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    var data = iconData[i];

                    writer.Write(entry.Width);
                    writer.Write(entry.Height);
                    writer.Write(entry.ColorCount);
                    writer.Write(entry.Reserved);
                    writer.Write(entry.Planes);
                    writer.Write(entry.BitCount);
                    writer.Write(data.Length);
                    writer.Write(offset);

                    offset += data.Length;
                }

                foreach (var data in iconData)
                {
                    writer.Write(data);
                }

                writer.Flush();
                return ms.ToArray();
            }

            internal static int CalculateQualityScore(IReadOnlyList<GroupIconEntry> entries)
            {
                if (entries == null || entries.Count == 0)
                {
                    return 0;
                }

                int maxArea = entries.Max(e => NormalizeDimension(e.Width) * NormalizeDimension(e.Height));
                int maxBitDepth = entries.Max(e => e.BitCount);
                return (maxArea * 1000) + maxBitDepth;
            }

            internal static int NormalizeDimension(byte value) => value == 0 ? 256 : value;
        }

        private readonly struct GroupIconEntry
        {
            internal GroupIconEntry(byte width, byte height, byte colorCount, byte reserved, ushort planes, ushort bitCount, uint bytesInRes, ushort resourceId)
            {
                Width = width;
                Height = height;
                ColorCount = colorCount;
                Reserved = reserved;
                Planes = planes;
                BitCount = bitCount;
                BytesInRes = bytesInRes;
                ResourceId = resourceId;
            }

            internal byte Width { get; }
            internal byte Height { get; }
            internal byte ColorCount { get; }
            internal byte Reserved { get; }
            internal ushort Planes { get; }
            internal ushort BitCount { get; }
            internal uint BytesInRes { get; }
            internal ushort ResourceId { get; }
        }

        private static class NativeMethods
        {
            [Flags]
            internal enum LoadLibraryFlags : uint
            {
                DONT_RESOLVE_DLL_REFERENCES = 0x0000_0001,
                LOAD_LIBRARY_AS_DATAFILE = 0x0000_0002,
                LOAD_WITH_ALTERED_SEARCH_PATH = 0x0000_0008,
                LOAD_IGNORE_CODE_AUTHZ_LEVEL = 0x0000_0010,
                LOAD_LIBRARY_AS_IMAGE_RESOURCE = 0x0000_0020,
                LOAD_LIBRARY_AS_DATAFILE_EXCLUSIVE = 0x0000_0040,
            }

            internal delegate bool EnumResourceNameProc(IntPtr hModule, IntPtr lpszType, IntPtr lpszName, IntPtr lParam);
            internal delegate bool EnumResourceLangProc(IntPtr hModule, IntPtr lpszType, IntPtr lpszName, ushort wIDLanguage, IntPtr lParam);

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            internal static extern SafeLibraryHandle LoadLibraryEx(string lpFileName, IntPtr hFile, LoadLibraryFlags dwFlags);

            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern bool FreeLibrary(IntPtr hModule);

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            internal static extern bool EnumResourceNames(IntPtr hModule, IntPtr lpszType, EnumResourceNameProc lpEnumFunc, IntPtr lParam);

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            internal static extern bool EnumResourceLanguages(IntPtr hModule, IntPtr lpszType, IntPtr lpszName, EnumResourceLangProc lpEnumFunc, IntPtr lParam);

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            internal static extern IntPtr FindResourceEx(IntPtr hModule, IntPtr lpType, IntPtr lpName, ushort wLanguage);

            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern IntPtr LoadResource(IntPtr hModule, IntPtr hResInfo);

            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern IntPtr LockResource(IntPtr hResData);

            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern uint SizeofResource(IntPtr hModule, IntPtr hResInfo);
        }

        private sealed class SafeLibraryHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            private SafeLibraryHandle()
                : base(true)
            {
            }

            protected override bool ReleaseHandle() => NativeMethods.FreeLibrary(handle);
        }
    }
}
