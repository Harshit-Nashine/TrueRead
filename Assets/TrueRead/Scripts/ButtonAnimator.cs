using UnityEngine;
using UnityEngine.EventSystems;
using DG.Tweening;

public class ButtonAnimator : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    public void OnPointerDown(PointerEventData e)
    {
        transform.DOScale(0.95f, 0.08f).SetEase(Ease.InQuad);
    }
    public void OnPointerUp(PointerEventData e)
    {
        transform.DOScale(1f, 0.15f).SetEase(Ease.OutBack);
    }
}