using AllModsSky.Core.Localization;
using AllModsSky.Core.Models;
using AllModsSky.Core.Services;
using Xunit;

namespace AllModsSky.Core.Tests;

/// <summary>
/// 「库里一套配置都没有」时的播种逻辑：只把启用中的模组收进新建的「默认」配置，
/// 停用的留在模组库（池子）里。
/// </summary>
public sealed class ModLibrarySeedTests
{
    [Fact]
    public void EmptyLibrary_CreatesDefaultProfile()
    {
        var store = new FakeLibraryStore(new LibraryState());
        var library = new ModLibrary(store);

        library.Load();

        var profile = Assert.Single(library.Profiles);
        Assert.Equal(L.T("默认"), profile.Name);
        Assert.Empty(profile.Entries);
        Assert.Empty(library.Mods);

        // 新库不该落在「无模组」上，否则「添加模组」没有地方放
        Assert.Equal(profile.Id, library.ActiveProfileId);
        Assert.Equal(1, store.SaveCount);
    }

    [Fact]
    public void ExistingProfile_IsNotSeededAgain()
    {
        var mod = Mod("Keep", enabled: true);
        var existing = new ModProfile { Name = "我的配置" };
        existing.Entries.Add(new ConfigEntry { ModId = mod.Id, Enabled = true });

        var store = new FakeLibraryStore(new LibraryState
        {
            Mods = new List<ModRecord> { mod },
            Profiles = new List<ModProfile> { existing },
            ActiveProfileId = existing.Id
        });

        var library = new ModLibrary(store);
        library.Load();

        var profile = Assert.Single(library.Profiles);
        Assert.Same(existing, profile);
        Assert.Equal("我的配置", profile.Name);
        Assert.Single(profile.Entries);
        Assert.Equal(mod.Id, profile.Entries[0].ModId);

        // 没有需要修正的东西，不该白写一次盘
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public void EnabledMods_MoveIntoDefaultProfile_DisabledStayInPool()
    {
        var enabled = new[] { Mod("A", true), Mod("B", true), Mod("C", true) };
        var disabled = new[] { Mod("D", false), Mod("E", false) };

        var store = new FakeLibraryStore(new LibraryState
        {
            Mods = enabled.Concat(disabled).ToList(),
            Profiles = new List<ModProfile>()
        });

        var library = new ModLibrary(store);
        library.Load();

        // 自动生成「默认」，里面只有 3 个启用中的模组
        var profile = Assert.Single(library.Profiles);
        Assert.Equal(L.T("默认"), profile.Name);
        Assert.Equal(3, profile.Entries.Count);
        Assert.Equal(Ids(enabled), Ids(profile.Entries));
        Assert.All(profile.Entries, e => Assert.True(e.Enabled));

        // 5 个模组都还在库里，启停状态没被播种逻辑改写
        Assert.Equal(5, library.Mods.Count);
        Assert.All(enabled, m => Assert.True(m.Enabled));
        Assert.All(disabled, m => Assert.False(m.Enabled));

        // 停用的两个留在模组库（不在当前配置里）
        Assert.Equal(Ids(disabled), Ids(library.PoolMods));
        Assert.Equal(Ids(enabled), Ids(library.ActiveMods));

        Assert.Equal(profile.Id, library.ActiveProfileId);
        Assert.Equal(1, store.SaveCount);
        Assert.NotNull(store.Saved);
        Assert.Single(store.Saved!.Profiles);
    }

    private static ModRecord Mod(string name, bool enabled) => new()
    {
        Name = name,
        FolderName = name,
        Enabled = enabled
    };

    private static string[] Ids(IEnumerable<ModRecord> mods) =>
        mods.Select(m => m.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray();

    private static string[] Ids(IEnumerable<ConfigEntry> entries) =>
        entries.Select(e => e.ModId).OrderBy(id => id, StringComparer.Ordinal).ToArray();

    /// <summary>内存里的 library.json：不碰磁盘，Save 只记录写进来的状态。</summary>
    private sealed class FakeLibraryStore : ILibraryStore
    {
        private readonly LibraryState? _state;

        public FakeLibraryStore(LibraryState? state) => _state = state;

        public LibraryState? Saved { get; private set; }
        public int SaveCount { get; private set; }

        public bool TryLoad(out LibraryState? state)
        {
            state = _state;
            return true;
        }

        public string BackupCorrupt() => "library.json.bak";

        public LibraryState? LoadLegacy() => null;

        public void Save(LibraryState state)
        {
            Saved = state;
            SaveCount++;
        }
    }
}
