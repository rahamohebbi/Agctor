using AgctorSDK.Host.Services.VideoWorkflow;
using Xunit;
namespace AgctorSDK.Host.IntegrationTests;
public sealed class SceneNativeSpeechTests
{
    [Theory]
    [InlineData("en", "English")]
    [InlineData("fa", "Persian")]
    [InlineData("es", "Spanish")]
    public void Explicit_speech_uses_H3_tags_without_touching_signs_or_words(string code,string name)
    {
        var original="The sign reads \"Welcome\". A woman (S1) says: \"Hello, my friend!\"";
        var formatted=SceneNativeSpeech.Format(original,code);
        Assert.Contains("sign reads \"Welcome\"",formatted);
        Assert.Contains("<d>["+name+"] Hello, my friend!</d>",formatted);
        Assert.Equal("Hello, my friend!",Assert.Single(SceneNativeSpeech.Lines(formatted)));
        Assert.Equal(formatted,SceneNativeSpeech.Format(formatted,code));
    }
    [Fact]
    public void A_written_sign_is_not_spoken_dialogue()
    {
        var text="The sign says: \"Welcome home.\"";
        Assert.Equal(text,SceneNativeSpeech.Format(text,"en"));
        Assert.Empty(SceneNativeSpeech.Lines(text));
    }
    [Fact]
    public void Conflicting_language_is_clarified_before_a_render()
    {
        Assert.NotNull(SceneNativeSpeech.PlanConcern("","",["She says: <d>[Spanish] Hola.</d>"],[15],"en"));
    }
    [Fact]
    public void Existing_tags_preserve_punctuation_inner_quotes_and_order()
    {
        var prompt="A (S1) says: <d>[English] She says \"hello\".</d> B (S2) replies: “Welcome home.”";
        var formatted=SceneNativeSpeech.Format(prompt,"en");
        Assert.Contains("<d>[English] She says \"hello\".</d>",formatted);
        Assert.Equal(new[]{"She says \"hello\".","Welcome home."},SceneNativeSpeech.Lines(formatted));
    }
    [Theory]
    [InlineData("none")][InlineData("unknown")][InlineData("auto")]
    public void Does_not_guess_language_or_create_speech_for_silent_clips(string language) =>
        Assert.Equal("She says: \"Hello.\"",SceneNativeSpeech.Format("She says: \"Hello.\"",language));
    [Theory]
    [InlineData("Do not leave.","Do leave.",false)]
    [InlineData("I am 18.","I am 80.",false)]
    [InlineData("Hello, my friend!","hello my friend",true)]
    [InlineData("Don't leave.","Don’t leave!",true)]
    [InlineData("Hello.","Hello. Hello.",false)]
    public void Checks_actual_words_not_only_language(string expected,string heard,bool result) =>
        Assert.Equal(result,SceneNativeSpeech.Matches(expected,heard));
    [Fact]
    public void Planner_cannot_rewrite_quoted_script_or_overload_short_clip()
    {
        Assert.NotNull(SceneNativeSpeech.PlanConcern("She says: \"Do not go.\"","",["She says: \"Go.\""],[15],"en"));
        Assert.Null(SceneNativeSpeech.PlanConcern("She says: \"Do not go.\"","",["She says: <d>[English] Do not go.</d>"],[15],"en"));
        Assert.NotNull(SceneNativeSpeech.PlanConcern("","",["She says: \"These words need more room than a single second allows.\""],[2],"en"));
    }
    [Fact]
    public void Old_requests_and_dedicated_narration_are_not_rewritten()
    {
        var clip=new SceneVideoClip {Prompt="She says: \"Hello.\"",VideoPrompt="SHOT ACTION\nShe says: \"Hello.\"\nCONTINUITY"};
        var state=new SceneVideoState {Input=new("request","workspace","Story"),ExpectedSpeechLanguage="en"};
        Assert.Equal(clip.VideoPrompt,SceneNativeSpeech.RenderPrompt(state,clip));
        clip.NativeSpeechVersion=1;
        Assert.Contains("<d>[English] Hello.</d>",SceneNativeSpeech.RenderPrompt(state,clip));
        state.Input=state.Input with {NarrationMode="dedicated"};
        Assert.Equal(clip.VideoPrompt,SceneNativeSpeech.RenderPrompt(state,clip));
    }
    [Fact]
    public void Explicit_correction_removes_old_dialogue_and_preserves_scene()
    {
        var clip=new SceneVideoClip {Prompt="In the forest she says: \"Walk away.\"",VideoPrompt="SHOT ACTION\nIn the forest she says: \"Walk away.\"\nEND",NativeSpeechVersion=1,SpeechInstruction="She says: \"Stay here.\""};
        var state=new SceneVideoState {Input=new("r","w","Story"),ExpectedSpeechLanguage="en"};
        var prompt=SceneNativeSpeech.RenderPrompt(state,clip);
        Assert.Contains("In the forest",prompt); Assert.DoesNotContain("Walk away.",prompt);
        Assert.Contains("<d>[English] Stay here.</d>",prompt);
        Assert.Equal("Stay here.",SceneNativeSpeech.Expected(clip));
        Assert.True(("All human speech must be intelligible English. Do not invent extra dialogue. "+SceneNativeSpeech.Direction+" Creator's audio correction: "+new string('x',2000)).Length<=2400);
    }
}
