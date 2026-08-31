using Humo.Shared;
using Humo.Shared.Enums;

namespace Humo.Shared.Tests;

public class MeatTypeDefaultsTests
{
    [Fact]
    public void Every_meat_type_has_a_usable_starting_weight()
    {
        // A blank weight field is the failure these defaults exist to prevent,
        // so "every value in the enum" is the contract, not "every value I
        // happened to write down".
        foreach (var meatType in Enum.GetValues<MeatType>())
        {
            var weight = MeatTypeDefaults.ForMeatType(meatType);

            Assert.True(
                double.IsFinite(weight) && weight > 0,
                $"{meatType} has no usable default weight: {weight}.");
        }
    }

    [Fact]
    public void An_unknown_enum_value_falls_back_instead_of_throwing()
    {
        // Casting an unmapped number is exactly what a future release, or a
        // corrupted row, produces. The form must still open.
        var weight = MeatTypeDefaults.ForMeatType((MeatType)1234);

        Assert.Equal(MeatTypeDefaults.FallbackWeightKg, weight);
    }

    [Fact]
    public void Other_falls_back_because_nothing_is_known_about_it()
    {
        Assert.Equal(MeatTypeDefaults.FallbackWeightKg, MeatTypeDefaults.ForMeatType(MeatType.Other));
    }

    [Theory]
    [InlineData(MeatType.Brisket, 6.0)]
    [InlineData(MeatType.PorkButt, 4.0)]
    [InlineData(MeatType.PorkRibs, 1.5)]
    [InlineData(MeatType.Sausage, 1.0)]
    public void Named_cuts_keep_their_documented_weights(MeatType meatType, double expectedKg)
    {
        // These feed the fire model's thermal load. Changing one silently would
        // shift predictions for every cook of that cut.
        Assert.Equal(expectedKg, MeatTypeDefaults.ForMeatType(meatType));
    }

    [Fact]
    public void The_defaults_are_kilograms_not_pounds()
    {
        // The whole storage layer is metric. A pounds figure slipping in here
        // would be plausible-looking and wrong by a factor of 2.2 -- so pin the
        // magnitude: no cut in this app is a 13 kg default.
        foreach (var meatType in Enum.GetValues<MeatType>())
        {
            Assert.InRange(MeatTypeDefaults.ForMeatType(meatType), 0.5, 10.0);
        }
    }
}
