using G915Fix.Core.Configuration;
using G915Fix.Core.Input;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace G915Fix.Core.Tests;

[TestClass]
public sealed class ConfigurationTests
{
    [TestMethod]
    public void Compiler_ResolvesHidTokensAndReportsInvalidValues()
    {
        var configuration = new AppConfiguration
        {
            Keyboard = new KeyboardFilterConfiguration
            {
                ExcludedKeys = ["Ctrl", "not-a-key"],
                MinimumRepeatIntervalMs = -1,
                Mode = "not-a-mode",
                PerKeyMinimumRepeatIntervalMs = new Dictionary<string, double>
                {
                    ["A"] = 12,
                    ["bad"] = 10
                }
            },
            Mouse = new MouseFilterConfiguration
            {
                ExcludedButtons = ["Left", "bad"],
                MinimumRepeatIntervalMs = double.NaN
            }
        };

        ConfigurationCompilationResult result = new ConfigurationCompiler().Compile(configuration);

        Assert.IsTrue(result.KeyboardOptions.ExcludedKeys.Contains(HidKeyboardUsage.LeftControl));
        Assert.IsTrue(result.KeyboardOptions.ExcludedKeys.Contains(HidKeyboardUsage.RightControl));
        Assert.AreEqual(TimeSpan.FromMilliseconds(12), result.KeyboardOptions.PerKeyMinimumRepeatIntervals[HidKeyboardUsage.A]);
        Assert.AreEqual(TimeSpan.FromMilliseconds(28), result.KeyboardOptions.MinimumRepeatInterval);
        Assert.AreEqual(KeyboardDebounceMode.BlockRepress, result.KeyboardOptions.Mode);
        Assert.IsTrue(result.MouseOptions.ExcludedButtons.Contains(MouseButton.Left));
        Assert.AreEqual(TimeSpan.FromMilliseconds(50), result.MouseOptions.MinimumRepeatInterval);
        Assert.AreEqual(6, result.Warnings.Count);
    }

    [TestMethod]
    public async Task JsonStore_BindsAndAtomicallyPersistsConfiguration()
    {
        string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string path = System.IO.Path.Combine(directory, "config.json");
        var store = new JsonAppConfigurationStore(path);
        var configuration = new AppConfiguration
        {
            DefaultProfile = "gaming",
            Keyboard = new KeyboardFilterConfiguration { MinimumRepeatIntervalMs = 12 },
            Games = new GameProfileConfiguration
            {
                ProfileMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["game"] = "gaming" }
            }
        };

        try
        {
            Assert.IsTrue((await store.SaveAsync(configuration)).Succeeded);
            ConfigurationLoadResult loaded = await store.LoadAsync();

            Assert.IsTrue(loaded.Succeeded);
            Assert.IsTrue(loaded.Exists);
            Assert.AreEqual("gaming", loaded.Configuration.DefaultProfile);
            Assert.AreEqual(12d, loaded.Configuration.Keyboard.MinimumRepeatIntervalMs);
            Assert.AreEqual("gaming", loaded.Configuration.Games.ProfileMap["game"]);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task JsonStore_ReturnsDefaultsForMissingOrMalformedFiles()
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"), "config.json");
        var store = new JsonAppConfigurationStore(path);
        ConfigurationLoadResult missing = await store.LoadAsync();
        Assert.IsFalse(missing.Exists);
        Assert.IsTrue(missing.Succeeded);

        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "{");
        ConfigurationLoadResult malformed = await store.LoadAsync();
        Assert.IsTrue(malformed.Exists);
        Assert.IsFalse(malformed.Succeeded);

        Directory.Delete(System.IO.Path.GetDirectoryName(path)!, recursive: true);
    }
}
