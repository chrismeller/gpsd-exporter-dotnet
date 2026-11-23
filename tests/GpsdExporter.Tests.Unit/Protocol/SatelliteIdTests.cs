using GpsdExporter.Protocol;

namespace GpsdExporter.Tests.Unit.Protocol;

public sealed class SatelliteIdTests
{
    private static Satellite Sat(int prn, int? gnss = null, int? sv = null, int? sig = null) =>
        new(prn, gnss, sv, sig, null, FieldValues<SatelliteField>.Empty);

    [Fact]
    public void Uses_gnssid_svid_and_sigid_when_supplied()
    {
        var id = SatelliteId.Of(Sat(3, 0, 3, 6));

        (id.GnssId, id.SvId, id.SigId, id.Prn).ShouldBe((0, 3, 6, (int?)null));
    }

    [Fact]
    public void T6_2_signals_of_one_satellite_have_distinct_identities()
    {
        SatelliteId.Of(Sat(3, 0, 3, 0)).ShouldNotBe(SatelliteId.Of(Sat(3, 0, 3, 6)));
    }

    [Fact]
    public void Prn_does_not_take_part_when_gnssid_and_svid_are_supplied()
    {
        SatelliteId.Of(Sat(3, 0, 3, 0)).ShouldBe(SatelliteId.Of(Sat(99, 0, 3, 0)));
    }

    [Fact]
    public void A_missing_sigid_is_part_of_the_identity()
    {
        SatelliteId.Of(Sat(3, 0, 3)).ShouldNotBe(SatelliteId.Of(Sat(3, 0, 3, 0)));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(0, null)]
    [InlineData(null, 3)]
    public void T6_3_falls_back_to_prn_without_both_gnssid_and_svid(int? gnss, int? sv)
    {
        var id = SatelliteId.Of(Sat(17, gnss, sv, 0));

        (id.GnssId, id.SvId, id.SigId, id.Prn).ShouldBe(((int?)null, (int?)null, (int?)null, 17));
    }

    [Theory]
    [InlineData(0, "gps")]
    [InlineData(1, "sbas")]
    [InlineData(2, "galileo")]
    [InlineData(3, "beidou")]
    [InlineData(4, "imes")]
    [InlineData(5, "qzss")]
    [InlineData(6, "glonass")]
    [InlineData(7, "navic")]
    [InlineData(8, "unknown")]
    [InlineData(-1, "unknown")]
    [InlineData(255, "unknown")]
    public void Constellation_comes_from_the_gnssid_table(int gnssId, string expected) =>
        Constellation.FromGnssId(gnssId).ShouldBe(expected);
}
