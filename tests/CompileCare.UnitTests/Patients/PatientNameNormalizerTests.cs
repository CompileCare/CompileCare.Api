using CompileCare.Application.Patients;

namespace CompileCare.UnitTests.Patients;

public sealed class PatientNameNormalizerTests
{
    [Theory]
    [InlineData("  Asha   R. ", "ASHA R.")]
    [InlineData("Anne\tMarie", "ANNE MARIE")]
    [InlineData("Ａｓｈａ", "ASHA")]
    [InlineData("  श्रुती  पाटील ", "श्रुती पाटील")]
    public void Normalize_AppliesCompatibilityCaseAndWhitespaceRules(string input, string expected)
    {
        Assert.Equal(expected, PatientNameNormalizer.Normalize(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public void Normalize_ReturnsNullForMissingName(string? input)
    {
        Assert.Null(PatientNameNormalizer.Normalize(input));
    }

    [Fact]
    public void Normalize_PreservesPunctuationAndDiacritics()
    {
        Assert.Equal("ANNE-MARIE", PatientNameNormalizer.Normalize("Anne-Marie"));
        Assert.NotEqual(
            PatientNameNormalizer.Normalize("JOSE"),
            PatientNameNormalizer.Normalize("JOSÉ"));
    }
}
