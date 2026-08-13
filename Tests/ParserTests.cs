using LnisAfsValidator.Infrastructure;
namespace LnisAfsValidator.Tests;
/// <summary>PocketSDR 로그에서 수신 증거와 경고가 정확히 추출되는지 검증한다.</summary>
public sealed class ParserTests
{
    [Fact] public void ParsesConfirmedAfsRecordsAndUsesFinalNsatField()
    {
        string[] lines = ["$LOG,1.000,AFSD,2,SIGNAL FOUND (42.0,1.0,0.1)", "$SB2,12.000,AFSD,2,ABCD,EF", "$SB3,12.000,AFSD,2,FRAME DECODED", "$SB4,12.000,AFSD,2,FRAME DECODED", "$POS,12.000,2025,7,1,2,3,4.500,-89.660000000,129.200000000,100.000,5,7"];
        var e = new PocketSdrLogParser().Parse(lines);
        Assert.Contains(2, e.AcquiredPrns); Assert.Contains(2, e.Sb2DecodedPrns); Assert.Equal(1, e.Sb3DecodedCount); Assert.Equal(1, e.Sb4DecodedCount);
        Assert.Equal(7, e.ObservedSatelliteCount); Assert.Equal(-89.66, e.Position!.LatitudeDegrees, 6); Assert.Equal(12, e.ReceiverRelativeTimeSeconds);
    }
    [Fact] public void MalformedRecognizedRecordIsWarningNotSuccess()
    {
        var e = new PocketSdrLogParser().Parse(["$POS,bad,2025,1,1,0,0,0,0,0,0,5,5"]);
        Assert.Null(e.Position); Assert.NotEmpty(e.ParserWarnings);
    }
}
