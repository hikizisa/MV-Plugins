using System;
using System.Collections.Generic;
using System.Linq;
using MapsetVerifier.Parser.Objects;

namespace MapsetVerifier.Plugin.SkinChecks.Model
{
    public static class SkinRegistry
    {
        public static readonly List<SkinSetDefinition> AllSets = new();

        static SkinRegistry()
        {
            RegisterOsuSets();
            RegisterTaikoSets();
            RegisterCatchSets();
            RegisterManiaSets();
            RegisterInterfaceSets();
        }

        private static void RegisterOsuSets()
        {
            // Cursor set
            AllSets.Add(new SkinSetDefinition(
                "Cursor Set",
                SkinMode.Standard,
                new List<SkinElementRequirement>
                {
                    new("cursor.png", isRequired: true, note: "Should always be included with cursortrail.png"),
                    new("cursortrail.png", isRequired: true, note: "Should always be included with cursor.png"),
                    new("cursormiddle.png", isRequired: false),
                    new("cursor-smoke.png", isRequired: false)
                }
            ));

            // Hitcircle set
            var hitcircleElements = new List<SkinElementRequirement>
            {
                new("hitcircle.png", isRequired: true),
                new("hitcircleoverlay{-n}.png", isRequired: true, isAnimatable: true),
                new("approachcircle.png", isRequired: true)
            };
            for (int i = 0; i <= 9; i++)
            {
                hitcircleElements.Add(new SkinElementRequirement($"default-{i}.png", isRequired: false, group: "ComboNumbers"));
            }
            AllSets.Add(new SkinSetDefinition("Hitcircle Set", SkinMode.Standard, hitcircleElements));

            // Slidertrack set
            AllSets.Add(new SkinSetDefinition(
                "Slidertrack Set",
                SkinMode.Standard,
                new List<SkinElementRequirement>
                {
                    new("sliderb.png", isRequired: true, isAnimatable: true),
                    new("sliderfollowcircle{-n}.png", isRequired: true, isAnimatable: true),
                    new("reversearrow.png", isRequired: true),
                    new("sliderpoint10.png", isRequired: false),
                    new("sliderpoint30.png", isRequired: false),
                    new("sliderstartcircle.png", isRequired: false, group: "SliderStart"),
                    new("sliderstartcircleoverlay{-n}.png", isRequired: false, isAnimatable: true, group: "SliderStart"),
                    new("sliderendcircle.png", isRequired: false, group: "SliderEnd"),
                    new("sliderendcircleoverlay{-n}.png", isRequired: false, isAnimatable: true, group: "SliderEnd")
                }
            ));

            // Hitburst set (Standard)
            AllSets.Add(new SkinSetDefinition(
                "Hitburst Set (osu!)",
                SkinMode.Standard,
                new List<SkinElementRequirement>
                {
                    new("hit0{-n}.png", isRequired: true, isAnimatable: true),
                    new("hit50{-n}.png", isRequired: true, isAnimatable: true),
                    new("hit100{-n}.png", isRequired: true, isAnimatable: true),
                    new("hit100k{-n}.png", isRequired: true, isAnimatable: true),
                    new("hit300{-n}.png", isRequired: true, isAnimatable: true),
                    new("hit300g{-n}.png", isRequired: true, isAnimatable: true),
                    new("hit300k{-n}.png", isRequired: true, isAnimatable: true)
                }
            ));

            // Hit lighting set
            AllSets.Add(new SkinSetDefinition(
                "Hit Lighting Set",
                SkinMode.Standard,
                new List<SkinElementRequirement>
                {
                    new("lighting{-n}.png", isRequired: true, isAnimatable: true)
                }
            ));

            // Spinner (New Style)
            AllSets.Add(new SkinSetDefinition(
                "Spinner Set (New Style)",
                SkinMode.Standard,
                new List<SkinElementRequirement>
                {
                    new("spinner-top.png", isRequired: true),
                    new("spinner-bottom.png", isRequired: true),
                    new("spinner-middle.png", isRequired: true),
                    new("spinner-middle2.png", isRequired: true),
                    new("spinner-glow.png", isRequired: true),
                    new("spinner-clear.png", isRequired: true),
                    new("spinner-spin.png", isRequired: false),
                    new("spinner-rpm.png", isRequired: false)
                }
            ));

            // Spinner (Old Style)
            AllSets.Add(new SkinSetDefinition(
                "Spinner Set (Old Style)",
                SkinMode.Standard,
                new List<SkinElementRequirement>
                {
                    new("spinner-background.png", isRequired: true),
                    new("spinner-circle.png", isRequired: true),
                    new("spinner-approachcircle.png", isRequired: true),
                    new("spinner-clear.png", isRequired: true),
                    new("spinner-spin.png", isRequired: false),
                    new("spinner-rpm.png", isRequired: false)
                }
            ));
        }

        private static void RegisterTaikoSets()
        {
            AllSets.Add(new SkinSetDefinition(
                "Hit Object Set (Taiko)",
                SkinMode.Taiko,
                new List<SkinElementRequirement>
                {
                    new("taiko-hitcircle.png", isRequired: true),
                    new("taiko-hitcircleoverlay.png", isRequired: true),
                    new("taiko-bigcircle.png", isRequired: true),
                    new("taiko-bigcircleoverlay.png", isRequired: true)
                }
            ));

            AllSets.Add(new SkinSetDefinition(
                "Hitburst Set (Taiko)",
                SkinMode.Taiko,
                new List<SkinElementRequirement>
                {
                    new("taiko-hit0.png", isRequired: true),
                    new("taiko-hit100.png", isRequired: true),
                    new("taiko-hit100k.png", isRequired: true),
                    new("taiko-hit300.png", isRequired: true),
                    new("taiko-hit300k.png", isRequired: true)
                }
            ));

            AllSets.Add(new SkinSetDefinition(
                "Pippidon Set (Taiko)",
                SkinMode.Taiko,
                new List<SkinElementRequirement>
                {
                    new("pippidonclear{-n}.png", isRequired: true, isAnimatable: true),
                    new("pippidonfail{-n}.png", isRequired: true, isAnimatable: true),
                    new("pippidondosdon{-n}.png", isRequired: true, isAnimatable: true),
                    new("pippidoidle{-n}.png", isRequired: true, isAnimatable: true),
                    new("pippidonkiai{-n}.png", isRequired: true, isAnimatable: true)
                }
            ));
        }

        private static void RegisterCatchSets()
        {
            AllSets.Add(new SkinSetDefinition(
                "Fruits Set (Catch)",
                SkinMode.Catch,
                new List<SkinElementRequirement>
                {
                    new("fruit-apple.png", isRequired: true),
                    new("fruit-apple-overlay.png", isRequired: true),
                    new("fruit-grapes.png", isRequired: true),
                    new("fruit-grapes-overlay.png", isRequired: true),
                    new("fruit-orange.png", isRequired: true),
                    new("fruit-orange-overlay.png", isRequired: true),
                    new("fruit-bananas.png", isRequired: true),
                    new("fruit-bananas-overlay.png", isRequired: true),
                    new("fruit-drop.png", isRequired: true),
                    new("fruit-drop-overlay.png", isRequired: true),
                    new("fruit-drop-empty.png", isRequired: false)
                }
            ));

            AllSets.Add(new SkinSetDefinition(
                "Catcher Set (Catch)",
                SkinMode.Catch,
                new List<SkinElementRequirement>
                {
                    new("fruit-catcher-idle.png", isRequired: true),
                    new("fruit-catcher-fail.png", isRequired: true),
                    new("fruit-catcher-kiai.png", isRequired: true)
                }
            ));
        }

        private static void RegisterManiaSets()
        {
            AllSets.Add(new SkinSetDefinition(
                "Hitburst Set (Mania)",
                SkinMode.Mania,
                new List<SkinElementRequirement>
                {
                    new("mania-hit0{-n}.png", isRequired: true, isAnimatable: true),
                    new("mania-hit50{-n}.png", isRequired: true, isAnimatable: true),
                    new("mania-hit100{-n}.png", isRequired: true, isAnimatable: true),
                    new("mania-hit200{-n}.png", isRequired: true, isAnimatable: true),
                    new("mania-hit300{-n}.png", isRequired: true, isAnimatable: true),
                    new("mania-hit300g{-n}.png", isRequired: true, isAnimatable: true)
                }
            ));

            AllSets.Add(new SkinSetDefinition(
                "Stage Set (Mania)",
                SkinMode.Mania,
                new List<SkinElementRequirement>
                {
                    new("stage-bottom.png", isRequired: false),
                    new("stage-hint.png", isRequired: false),
                    new("stage-light.png", isRequired: false),
                    new("stage-left.png", isRequired: false),
                    new("stage-right.png", isRequired: false)
                }
            ));
        }

        private static void RegisterInterfaceSets()
        {
            AllSets.Add(new SkinSetDefinition(
                "Scorebar Set",
                SkinMode.General,
                new List<SkinElementRequirement>
                {
                    new("scorebar-bg.png", isRequired: true),
                    new("scorebar-colour{-n}.png", isRequired: true, isAnimatable: true),
                    new("scorebar-ki.png", isRequired: true),
                    new("scorebar-kidanger.png", isRequired: true),
                    new("scorebar-kidanger2.png", isRequired: true)
                }
            ));

            var scoreDigits = new List<SkinElementRequirement>();
            for (int i = 0; i <= 9; i++)
            {
                scoreDigits.Add(new SkinElementRequirement($"score-{i}.png", isRequired: false, group: "ScoreDigits"));
            }
            scoreDigits.Add(new SkinElementRequirement("score-percent.png", isRequired: false, group: "ScoreDigits"));
            scoreDigits.Add(new SkinElementRequirement("score-x.png", isRequired: false, group: "ScoreDigits"));
            AllSets.Add(new SkinSetDefinition("Score Numbers Set", SkinMode.General, scoreDigits));

            AllSets.Add(new SkinSetDefinition(
                "Section Indicator Set",
                SkinMode.General,
                new List<SkinElementRequirement>
                {
                    new("section-pass.png", isRequired: true),
                    new("section-fail.png", isRequired: true)
                }
            ));

            AllSets.Add(new SkinSetDefinition(
                "Pause Screen Set",
                SkinMode.General,
                new List<SkinElementRequirement>
                {
                    new("pause-overlay.png", isRequired: true),
                    new("pause-continue.png", isRequired: true),
                    new("pause-retry.png", isRequired: true),
                    new("pause-back.png", isRequired: true),
                    new("fail-background.png", isRequired: false)
                }
            ));

            AllSets.Add(new SkinSetDefinition(
                "Countdown Set",
                SkinMode.General,
                new List<SkinElementRequirement>
                {
                    new("count1.png", isRequired: true),
                    new("count2.png", isRequired: true),
                    new("count3.png", isRequired: true),
                    new("go.png", isRequired: true),
                    new("ready.png", isRequired: true)
                }
            ));
        }
    }
}
