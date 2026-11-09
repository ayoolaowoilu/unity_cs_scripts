using UnityEngine;

/// <summary>
/// Unity only sends OnAnimatorMove to scripts that sit on the same GameObject as the Animator.
/// This tiny component forwards it to the player / enemy controller, so the Animator can stay
/// on the character model (a child) while the controller moves the real character.
/// Added automatically at runtime when Use Root Motion is ticked.
/// </summary>
[DisallowMultipleComponent]
public class RootMotionRelay : MonoBehaviour
{
    public System.Action onAnimatorMove;

    void OnAnimatorMove()
    {
        if (onAnimatorMove != null) onAnimatorMove();
    }

    public static RootMotionRelay Attach(Animator animator)
    {
        var relay = animator.GetComponent<RootMotionRelay>();
        if (relay == null) relay = animator.gameObject.AddComponent<RootMotionRelay>();
        return relay;
    }
}