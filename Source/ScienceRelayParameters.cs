#region license
/*The MIT License (MIT)

ScienceRelayParameters - In game settings for Science Transfer

Copyright (c) 2016 DMagic

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
*/
#endregion

namespace ScienceRelay
{
	public class ScienceRelayParameters : GameParameters.CustomParameterNode
	{
		[GameParameters.CustomParameterUI("Require Science Lab on Target Vessel", autoPersistance = true)]
		public bool requireMPL = true;
		[GameParameters.CustomParameterUI("Data Transmission Boost", toolTip = "Boost data transmission based on connection strength", autoPersistance = true)]
		public bool transmissionBoost = true;
		[GameParameters.CustomParameterUI("Require Crewed Science Lab For Boost", autoPersistance = true)]
		public bool requireMPLForBoost = false;
		[GameParameters.CustomFloatParameterUI("Transmission Boost Penalty", toolTip = "Amount by which transmission boost is reduced as compared to sending data home", minValue = 0, maxValue = 1, displayFormat = "P0", autoPersistance = true)]
		public float transmissionPenalty = 0.5f;
		[GameParameters.CustomParameterUI("Show Transmission Warnings", toolTip = "Show warnings for non-repeatable experiments", autoPersistance = true)]
		public bool showTransmitWarning;

		public override void SetDifficultyPreset(GameParameters.Preset preset)
		{
			switch (preset)
			{
				case GameParameters.Preset.Easy:
					transmissionBoost = true;
					requireMPLForBoost = false;
					requireMPL = false;
					transmissionPenalty = 0;
					break;
				case GameParameters.Preset.Normal:
					transmissionBoost = true;
					requireMPLForBoost = false;
					requireMPL = false;
					transmissionPenalty = 0.25f;
					break;
				case GameParameters.Preset.Moderate:
					transmissionBoost = true;
					requireMPLForBoost = true;
					requireMPL = false;
					transmissionPenalty = 0.5f;
					break;
				case GameParameters.Preset.Hard:
					transmissionBoost = true;
					requireMPLForBoost = true;
					requireMPL = true;
					transmissionPenalty = 0.75f;
					break;
				case GameParameters.Preset.Custom:
					break;
			}
		}

		public override bool Enabled(System.Reflection.MemberInfo member, GameParameters parameters)
		{
			if (member.Name == "requireMPLForBoost")
				return !requireMPL && transmissionBoost;
			else if (member.Name == "transmissionPenalty")
				return transmissionBoost;

			return base.Enabled(member, parameters);
		}

		public override GameParameters.GameMode GameMode
		{
			get { return GameParameters.GameMode.SCIENCE | GameParameters.GameMode.CAREER; }
		}

		public override bool HasPresets
		{
			get { return true; }
		}

		public override string Section
		{
			get { return "DMagic Mods"; }
		}

		public override int SectionOrder
		{
			get { return 2; }
		}

		public override string Title
		{
			get { return "Science Relay"; }
		}
	}
}
