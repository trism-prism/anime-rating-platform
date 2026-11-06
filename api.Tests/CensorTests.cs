using api.Services;

namespace api.Tests;

public class CensorTests
{
    [Theory]
    [InlineData("you are a faggot", "you are a ******")]
    [InlineData("FAGGOT", "******")]
    [InlineData("niggger", "*******")] // repeated letters
    [InlineData("n1gg3r and f@gg0t", "****** and ******")] // look-alikes
    [InlineData("two kikes here", "two ***** here")] // plural
    [InlineData("retard, retarded!", "******, ********!")]
    [InlineData("(chink)", "(*****)")]
    public void Slurs_become_asterisks_of_equal_length(string input, string expected)
    {
        Assert.Equal(expected, Censor.Mask(input));
        Assert.Equal(input.Length, Censor.Mask(input).Length);
    }

    [Theory]
    [InlineData("What the fuck, this shit is damn bad")] // plain swearing stays
    [InlineData("a raccoon, the spice, a class act, Cocoon, Scunthorpe")] // whole words only
    [InlineData("")]
    [InlineData("A perfectly nice review. 10/10, would watch again.")]
    public void Everything_else_is_left_alone(string input) => Assert.Equal(input, Censor.Mask(input));

    [Fact]
    public void Long_runs_of_one_letter_do_not_hang()
    {
        var text = "n" + new string('i', 5000) + new string('g', 5000);
        Assert.Equal(text, Censor.Mask(text));
    }
}
