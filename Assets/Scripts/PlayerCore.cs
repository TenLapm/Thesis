using UnityEngine;
using UnityEngine.Events;

public class PlayerCore : MonoBehaviour
{
    [Header("Core Parameters")]
    public int maxHealth = 10;
    private int currentHealth;

    [Header("Match Events")]
    // Broadcasting health updates allows any UI Canvas or VFX manager to listen passively
    public UnityEvent<int> OnHealthChanged;
    public UnityEvent OnGameOver;

    private bool isCoreDestroyed = false;

    void Start()
    {
        currentHealth = maxHealth;
        OnHealthChanged?.Invoke(currentHealth);
    }

    public void TakeDamage(int damage)
    {
        if (isCoreDestroyed) return;

        currentHealth -= damage;
        Debug.Log($"Core took damage! Current Integrity: {currentHealth}/{maxHealth}");

        OnHealthChanged?.Invoke(currentHealth);

        if (currentHealth <= 0)
        {
            ExecuteGameOver();
        }
    }

    private void ExecuteGameOver()
    {
        isCoreDestroyed = true;
        Debug.LogWarning("GAME OVER! The core integrity has collapsed.");

        OnGameOver?.Invoke();

        // Halt game time simulation
        Time.timeScale = 0f;
    }
}