using G915Fix.Core.Configuration;
using G915Fix.Core.Profiles;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace G915Fix.Core.Tests;

[TestClass]
public sealed class ProfileTests
{
    [TestMethod]
    public async Task ProfileService_UsesPersistedProfileAndKeepsSelectionInBaseConfig()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string basePath = Path.Combine(directory, "config.json");
        string gamingPath = Path.Combine(directory, "gaming.json");
        var baseProfile = new ProfileDescriptor("config", basePath, true);
        var gamingProfile = new ProfileDescriptor("gaming", gamingPath);
        var store = new JsonProfileStore(directory);

        try
        {
            await store.SaveAsync(baseProfile, new AppConfiguration { DefaultProfile = "gaming" });
            await store.SaveAsync(gamingProfile, new AppConfiguration
            {
                Keyboard = new KeyboardFilterConfiguration { MinimumRepeatIntervalMs = 12 }
            });
            await File.WriteAllTextAsync(Path.Combine(directory, "not-a-profile.json"), "{\"hello\":true}");

            var service = new AppProfileService(store, baseProfile);
            ProfileActivationResult initialized = await service.InitializeAsync();

            Assert.IsTrue(initialized.Succeeded);
            Assert.AreEqual("gaming", service.ActiveProfile!.Name);
            Assert.AreEqual(12d, service.ActiveConfig!.Keyboard.MinimumRepeatIntervalMs);
            CollectionAssert.AreEqual(new[] { "config", "gaming" }, (await service.ListProfilesAsync()).Select(profile => profile.Name).ToArray());

            ProfileActivationResult activated = await service.ActivateAsync(baseProfile, persistAsDefault: true);
            Assert.IsTrue(activated.Succeeded);
            Assert.AreEqual("config", service.ActiveProfile!.Name);
            AppConfiguration savedBase = await store.LoadAsync(baseProfile);
            Assert.IsNull(savedBase.DefaultProfile);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task ProfileService_FallsBackToBaseWhenSelectedProfileIsInvalid()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var baseProfile = new ProfileDescriptor("config", Path.Combine(directory, "config.json"), true);
        var store = new JsonProfileStore(directory);

        try
        {
            await store.SaveAsync(baseProfile, new AppConfiguration { DefaultProfile = "gaming" });
            await File.WriteAllTextAsync(Path.Combine(directory, "gaming.json"), "{\"Keyboard\":{\"MinimumRepeatIntervalMs\":\"not-a-number\"}}");
            var service = new AppProfileService(store, baseProfile);

            ProfileActivationResult result = await service.InitializeAsync();

            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual("config", result.ActiveProfile!.Name);
            StringAssert.Contains(result.Message, "base configuration");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task ProfileService_FallsBackToBaseWhenSelectedProfileIsMissing()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var baseProfile = new ProfileDescriptor("config", Path.Combine(directory, "config.json"), true);
        var store = new JsonProfileStore(directory);

        try
        {
            await store.SaveAsync(baseProfile, new AppConfiguration { DefaultProfile = "missing" });
            var service = new AppProfileService(store, baseProfile);

            ProfileActivationResult result = await service.InitializeAsync();

            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual("config", result.ActiveProfile!.Name);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
