using SatisfactorySaveEditor.Core.Properties;

namespace SatisfactorySaveEditor.Core.Tests;

public class ValidationTests
{
    [Fact]
    public void ValidSave_PassesValidation()
    {
        var save = SaveFile.Load(SampleSaves.Bytes("sample.sav"));

        var bytes = save.ToValidatedBytes();

        Assert.Equal(save.SerializeBody(), SaveFile.Load(bytes).SerializeBody());
    }

    [Fact]
    public void EditThatBreaksAnObject_IsRefused()
    {
        var save = SaveFile.Load(SampleSaves.Bytes("sample.sav"));
        var manager = save.Body.AllObjects.First(o => o.ClassName == "BP_SchematicManager_C");
        // An int inside an object-reference array serializes, but cannot be read back as a reference.
        manager.Properties.Get<ArrayValue>("mPurchasedSchematics")!.Elements.Add(new IntValue(123456));

        var ex = Assert.Throws<SaveValidationException>(() => save.ToValidatedBytes());
        Assert.Contains("SchematicManager", ex.Message);
    }

    [Fact]
    public void RefusedSave_LeavesExistingFileUntouched()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sse-validation-{Guid.NewGuid():N}.sav");
        File.WriteAllBytes(path, SampleSaves.Bytes("sample.sav"));
        try
        {
            var save = SaveFile.Load(path);
            save.Body.AllObjects.First(o => o.ClassName == "BP_SchematicManager_C")
                .Properties.Get<ArrayValue>("mPurchasedSchematics")!.Elements.Add(new IntValue(1));

            Assert.Throws<SaveValidationException>(() => save.Save(path));
            Assert.Equal(SampleSaves.Bytes("sample.sav"), File.ReadAllBytes(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
