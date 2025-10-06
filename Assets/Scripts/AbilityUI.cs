using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class AbilityUI : MonoBehaviour
{
    public static AbilityUI Instance { get; private set; }

    [SerializeField] private RawImage[] icons; // e.g. [0] sprint, [1] special
    [SerializeField] private Image[] cooldownImages;

    private Coroutine[] cooldownRoutines;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        cooldownRoutines = new Coroutine[cooldownImages.Length];
    }

    public void StartCooldown(int index, float duration)
    {
        if (index < 0 || index >= cooldownImages.Length) return;

        if (cooldownRoutines[index] != null)
            StopCoroutine(cooldownRoutines[index]);

        cooldownRoutines[index] = StartCoroutine(CooldownCoroutine(index, duration));
    }

    private IEnumerator CooldownCoroutine(int index, float duration)
    {
        cooldownImages[index].fillMethod = Image.FillMethod.Vertical;
        cooldownImages[index].fillOrigin = (int)Image.OriginVertical.Bottom;
        cooldownImages[index].fillAmount = 1f;

        float timer = 0f;
        while (timer < duration)
        {
            timer += Time.deltaTime;
            cooldownImages[index].fillAmount = 1f - (timer / duration);
            yield return null;
        }

        cooldownImages[index].fillAmount = 0f;
    }

    public void SetAbilityIcon(int index, Texture icon)
    {
        if (index < 0 || index >= icons.Length || icon == null) return;
        icons[index].texture = icon;
    }

}
