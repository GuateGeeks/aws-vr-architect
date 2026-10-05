using NUnit.Framework;
namespace GuateGeeks.AwsVr.Tests
{
    public sealed class RealtimeCostTests
    {
        [Test] public void CachedAndCancelledUsageIsCountedOnceAtTheActualModelRate()
        {
            var meter=new RealtimeCost();
            var response=RealtimeProtocol.Parse("{\"type\":\"response.done\",\"response\":{\"id\":\"a\",\"status\":\"cancelled\",\"usage\":{\"input_token_details\":{\"audio_tokens\":1000,\"text_tokens\":1000,\"cached_tokens\":1000,\"cached_tokens_details\":{\"audio_tokens\":500,\"text_tokens\":500}},\"output_token_details\":{\"audio_tokens\":1000,\"text_tokens\":1000}}}}").response;
            meter.AddResponse("gpt-realtime-2.1-mini",response);meter.AddResponse("gpt-realtime-2.1-mini",response);
            Assert.AreEqual(.02788,meter.EstimatedUsd,.00000001);Assert.AreEqual(1,meter.Responses);
            response.id="b";meter.AddResponse("gpt-realtime-2.1",response);
            Assert.AreEqual(.13428,meter.EstimatedUsd,.00000001);Assert.IsFalse(meter.Incomplete);
        }
        [Test] public void TranscriptionIsSeparateAndMissingUsageDoesNotPretendToBeFree()
        {
            var meter=new RealtimeCost();
            var usage=new RealtimeProtocol.Usage{total_tokens=1200,output_tokens=200,input_token_details=new RealtimeProtocol.TokenDetails{audio_tokens=1000}};
            meter.AddTranscription("one",usage);meter.AddTranscription("one",usage);
            Assert.AreEqual(.00225,meter.EstimatedUsd,.00000001);
            meter.AddResponse("unknown",new RealtimeProtocol.Response{id="missing"});
            Assert.IsTrue(meter.Incomplete);Assert.That(meter.Summary,Does.Contain("parcial"));
        }
    }
}
