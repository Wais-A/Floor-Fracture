using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace FloorFracture.Editor
{
    public static partial class GameplaySmoke
    {
        static IEnumerator VerifyOfflineNarration()
        {
            var speaker = game.transform.Find("Offline narration").GetComponent<AudioSource>();
            var narration = game.GetComponent<OfflineNarration>();
            var body = game.Panel.transform.Find("Floor Fracture Menu/Instructions").GetComponent<Text>();
            Require(!speaker.playOnAwake && !speaker.spatialize && speaker.spatialBlend == 0f,
                "The narrator should play locally as a centered menu voice.");
            foreach (var page in new[] { GameStage.PracticeStep, GameStage.PracticeStone, GameStage.Results })
            {
                var clip = Resources.Load<AudioClip>("Narration/" + page);
                Require(clip && clip.channels == 1 && clip.frequency == 22050 && clip.length > 1 && clip.length < 5,
                    "Missing or invalid local voice recording for " + page);
                var samples = new float[clip.samples];
                Require(clip.GetData(samples,0) && samples.Max(s => Mathf.Abs(s)) > .2f,
                    "The local voice recording contains no audible waveform for " + page);
            }
            int volume = PlayerPrefs.GetInt("ff.volume",3), motion = PlayerPrefs.GetInt("ff.reduced",0);
            int tile = game.State.Width * (game.State.Height/2) + game.State.Width/2;
            StandOn(tile);
            Choose("Tutorial");
            Require(!speaker.clip && body.text.Contains(OfflineNarration.StepCaption), "Fresh settings should show captions with narration off.");
            Choose("Go to game"); Choose("Main menu"); Choose("Settings");
            Choose("Narration");
            Require(PlayerPrefs.GetInt("ff.narration") == 1 && PlayerPrefs.GetInt("ff.volume",3) == volume,
                "Turning narration on changed the master volume or did not save.");
            if (volume == 0) Choose("Volume");
            int audibleVolume = PlayerPrefs.GetInt("ff.volume",3);
            Choose("Back"); Choose("Tutorial");
            yield return Frames(2);
            ExpectSpoken(GameStage.PracticeStep,OfflineNarration.StepCaption);
            SetHead(new Pose(head.position + game.BoardView.Board.right*.22f,head.rotation));
            yield return Frames(3);
            Require(!speaker.clip,"Stepping away did not cancel the tutorial voice.");
            Choose("Try the stone"); ExpectSpoken(GameStage.PracticeStone,OfflineNarration.StoneCaption);
            narration.Configure(true,0);
            Require(!speaker.clip && !speaker.isPlaying,"Live master mute left the current line playing.");
            narration.Configure(true,audibleVolume/4f);
            Choose("Practice stepping"); ExpectSpoken(GameStage.PracticeStep,OfflineNarration.StepCaption);
            narration.enabled = false;
            Require(!speaker.clip,"Disabling narration left the recording queued.");
            narration.enabled = true;
            Choose("Try the stone"); ExpectSpoken(GameStage.PracticeStone,OfflineNarration.StoneCaption);
            Choose("Go to game");
            StandOn(tile); Choose("Start wave"); yield return Stage(GameStage.Countdown);
            Require(!speaker.clip,"Narration continued into a timed wave.");
            game.gameObject.SendMessage("OnApplicationPause",true);
            game.gameObject.SendMessage("OnApplicationPause",false);
            Choose("Show board"); Choose("Main menu"); Choose("Settings");
            while (PlayerPrefs.GetInt("ff.volume",3) != 0) Choose("Volume");
            Require(PlayerPrefs.GetInt("ff.narration") == 1,"Muting the master changed the narration preference.");
            Choose("Back"); Choose("Tutorial");
            Require(!speaker.clip && body.text.Contains(OfflineNarration.StepCaption),"A muted tutorial lost its caption or queued audio.");
            Choose("Go to game"); Choose("Main menu"); Choose("Settings");
            while (PlayerPrefs.GetInt("ff.volume",3) != audibleVolume) Choose("Volume");
            if (motion == 0) Choose("Fall motion");
            Choose("Back"); Choose("Development"); game.Ending.SetDevelopmentEnding(0);
            Choose("Preview loss"); game.Ending.Skip(); yield return Stage(GameStage.Results);
            Require(game.Ending.LastWasSkipped && !speaker.clip && body.text.Contains(OfflineNarration.ResultsCaption),
                "Skipping a fall should show the Results caption silently.");
            Choose("Restart"); Choose("Main menu"); Choose("Development"); Choose("Preview loss");
            yield return Stage(GameStage.Results,8);
            Require(!game.Ending.LastWasSkipped,"The previous Skip carried into a new fall.");
            ExpectSpoken(GameStage.Results,OfflineNarration.ResultsCaption);
            Choose("Restart");
            Require(!speaker.clip && !speaker.isPlaying,"Restart overlapped the Results voice with the new game.");
            game.Ending.SetDevelopmentEnding(-1);
            Choose("Main menu"); Choose("Settings"); Choose("Narration");
            while (PlayerPrefs.GetInt("ff.volume",3) != volume) Choose("Volume");
            if (motion == 0) Choose("Fall motion");
            Choose("Back");
            Record("Three non-silent local narration clips matched the tutorial and Results captions. Fresh settings, master mute, and skipped Results were silent. Stepping away, disabling narration, entering gameplay, and restarting stopped playback.");

            void ExpectSpoken(GameStage page, string caption)
            {
                Require(speaker.isPlaying && speaker.clip == Resources.Load<AudioClip>("Narration/" + page) &&
                    Mathf.Abs(speaker.volume - audibleVolume/4f) < .001f && body.text.Contains(caption),
                    "The local spoken clip, volume or on-screen caption disagreed at " + page);
            }
        }
    }
}
