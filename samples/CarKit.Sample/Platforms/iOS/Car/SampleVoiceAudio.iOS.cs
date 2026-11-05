using AVFoundation;

namespace CarKit.Sample.Car;

// Stage 7: the microphone and AVAudioSession belong to the conversational app (spec: "Category decisions (Stage 7)"):
// PlayAndRecord while listening, deactivation with NotifyOthersOnDeactivation afterwards (other audio apps resume).
// The sample does not record: a real app starts speech recognition here, and an app with the Media module then
// returns to the Playback category.
public sealed partial class SampleScreens
{
    private bool _listening;

    partial void BeginListening()
    {
        var audio = AVAudioSession.SharedInstance();
        var error = audio.SetCategory(AVAudioSessionCategory.PlayAndRecord, AVAudioSessionCategoryOptions.DuckOthers | AVAudioSessionCategoryOptions.AllowBluetooth)
            ?? audio.SetActive(true);
        _listening = error is null;
        SampleDiagnostics.Log(error is null ? "Microphone: PlayAndRecord active" : $"Microphone: {error.LocalizedDescription}");
    }

    partial void EndListening()
    {
        if (!_listening)
        {
            return;
        }

        _listening = false;
        AVAudioSession.SharedInstance().SetActive(false, AVAudioSessionSetActiveOptions.NotifyOthersOnDeactivation);
    }
}
