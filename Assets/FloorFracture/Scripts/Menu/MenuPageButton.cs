using System.Collections.Generic;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FloorFracture
{
    // A page can change while a hand is pinched, for example when a room finishes
    // loading. The same pooled button then represents a different action.
    public sealed class MenuPageButton : Button
    {
        private readonly HashSet<int> pressesOnThisPage = new();

        internal void ClearPendingPresses() => pressesOnThisPage.Clear();

        public override void OnPointerDown(PointerEventData input)
        {
            if (input.button == PointerEventData.InputButton.Left && IsInteractable())
                pressesOnThisPage.Add(input.pointerId);
            base.OnPointerDown(input);
        }

        public override void OnPointerClick(PointerEventData input)
        {
            if (pressesOnThisPage.Remove(input.pointerId)) base.OnPointerClick(input);
        }

        protected override void OnDisable()
        {
            ClearPendingPresses();
            base.OnDisable();
        }
    }
}
