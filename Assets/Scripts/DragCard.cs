using System;
using UnityEngine;
using UnityEngine.EventSystems;

// Something on the HUD that can be clicked, dragged off, dropped on, and hovered
// (2026-10-14): a bench slot, a gear inventory slot, a card of the shop. It carries no
// rule; it tells its owner what the mouse did and the owner asks PlayerBuilder.
public class DragCard : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    public enum Kind { Bench, Gear, Offer, Spare }

    public Kind kind;
    public int slot;

    public Action<DragCard> Clicked, DragBegan, DragEnded, Entered, Exited;

    public void OnBeginDrag(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left) DragBegan?.Invoke(this); }

    // Needed for Unity to send the begin and end of a drag at all.
    public void OnDrag(PointerEventData e) { }

    public void OnEndDrag(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left) DragEnded?.Invoke(this); }

    public void OnPointerClick(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left && !e.dragging) Clicked?.Invoke(this); }

    public void OnPointerEnter(PointerEventData e) { Entered?.Invoke(this); }

    public void OnPointerExit(PointerEventData e) { Exited?.Invoke(this); }
}
