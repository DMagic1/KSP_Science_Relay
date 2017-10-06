#region license
/*The MIT License (MIT)

ScienceRelayDialog - A small script attached to the Experiment Results Dialog prefab

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

using UnityEngine;
using UnityEngine.UI;
using KSP.UI.Screens.Flight.Dialogs;

namespace ScienceRelay
{
	public class ScienceRelayDialog : MonoBehaviour
	{
		public static EventData<ExperimentsResultDialog> onDialogSpawn = new EventData<ExperimentsResultDialog>("onDialogSpawn");
		public static EventData<ExperimentsResultDialog> onDialogClose = new EventData<ExperimentsResultDialog>("onDialogClose");

		private ExperimentsResultDialog dialog;

		public Button buttonNext;
		public Button buttonPrev;
		public Button buttonTransfer;

		private void Start()
		{
			dialog = gameObject.GetComponentInParent<ExperimentsResultDialog>();

			if (dialog == null)
			{
				Destroy(this);
				return;
			}

			if (ScienceRelay.Instance != null)
			{
				if (buttonNext != null)
					buttonNext.onClick.AddListener(ScienceRelay.Instance.onPageChange);

				if (buttonPrev != null)
					buttonPrev.onClick.AddListener(ScienceRelay.Instance.onPageChange);

				if (buttonTransfer != null)
					buttonTransfer.onClick.AddListener(ScienceRelay.Instance.onTransfer);
			}

			onDialogSpawn.Fire(dialog);
		}

		private void OnDestroy()
		{
			onDialogClose.Fire(dialog);
		}
	}
}
