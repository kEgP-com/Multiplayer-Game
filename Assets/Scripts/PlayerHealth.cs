using System.Collections;
using UnityEngine;
#if PHOTON_UNITY_NETWORKING
using Photon.Pun;
#endif

/// <summary>
/// Handles player-specific health feedback, screen damage flash, health bar HUD,
/// and triggers the End Game (Game Over) screen when health reaches 0.
/// </summary>
[RequireComponent(typeof(Health))]
[DisallowMultipleComponent]
public class PlayerHealth : MonoBehaviour
{
    public static bool IsGameOver { get; private set; } = false;

#if PHOTON_UNITY_NETWORKING
    private PhotonView photonView;
#endif

    private Health health;
    private Rigidbody rb;
    private float damageFlashTimer = 0f;
    private const float DamageFlashDuration = 0.25f;
    private float matchStartTime = 0f;
    private float timeSurvived = 0f;

    private void Awake()
    {
#if PHOTON_UNITY_NETWORKING
        photonView = GetComponent<PhotonView>();
#endif
        health = GetComponent<Health>();
        rb = GetComponent<Rigidbody>();

        if (health != null)
        {
            health.OnDamaged += OnDamaged;
            health.OnDeath += OnDeath;
        }

        IsGameOver = false;
        matchStartTime = Time.time;
    }

    private void Update()
    {
        if (damageFlashTimer > 0f)
        {
            damageFlashTimer -= Time.deltaTime;
        }

        if (!IsGameOver)
        {
            timeSurvived = Time.time - matchStartTime;
        }
    }

    private void OnDamaged(float amount, Vector3 hitPoint, Vector3 hitNormal)
    {
#if PHOTON_UNITY_NETWORKING
        if (photonView != null && !photonView.IsMine) return;
#endif
        damageFlashTimer = DamageFlashDuration;
    }

    private void OnDeath()
    {
#if PHOTON_UNITY_NETWORKING
        if (photonView != null && !photonView.IsMine) return;
#endif
        TriggerGameOver();
    }

    private void TriggerGameOver()
    {
        IsGameOver = true;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (rb != null)
        {
#if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = Vector3.zero;
#else
            rb.velocity = Vector3.zero;
#endif
        }

        Debug.Log("[PlayerHealth] Player has died! End Game (Game Over) triggered.");
    }

    public void RestartGame()
    {
        IsGameOver = false;
        matchStartTime = Time.time;
        timeSurvived = 0f;

        // Reset player state
        transform.position = new Vector3(0f, 1.5f, 0f);
        if (rb != null)
        {
#if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = Vector3.zero;
#else
            rb.velocity = Vector3.zero;
#endif
        }

        if (health != null)
        {
            health.ResetHealth();
        }

        // Reset Spawner and active enemies
        if (EnemySpawner.Instance != null)
        {
            EnemySpawner.Instance.ResetAllEnemies();
        }

        // Lock cursor back into game
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        Debug.Log("[PlayerHealth] Game restarted! Combat resumed.");
    }

    private void OnGUI()
    {
#if PHOTON_UNITY_NETWORKING
        if (photonView != null && !photonView.IsMine) return;
        // If connected to Photon but not in room yet (in lobby), skip gameplay HUD
        if (PhotonNetwork.IsConnected && !PhotonNetwork.InRoom) return;
#endif
        if (health == null) return;

        // 1. Red Screen Damage Flash
        if (damageFlashTimer > 0f)
        {
            float alpha = (damageFlashTimer / DamageFlashDuration) * 0.35f;
            Color prevColor = GUI.color;
            GUI.color = new Color(1f, 0f, 0f, alpha);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = prevColor;
        }

        // 2. Health Bar HUD (bottom-left)
        if (!IsGameOver)
        {
            DrawHealthHUD();
        }
        else
        {
            DrawGameOverScreen();
        }
    }

    private void DrawHealthHUD()
    {
        float barWidth = 220f;
        float barHeight = 22f;
        float x = 25f;
        float y = Screen.height - 48f;

        // Background
        GUI.color = new Color(0.08f, 0.08f, 0.1f, 0.85f);
        GUI.DrawTexture(new Rect(x - 2, y - 2, barWidth + 4, barHeight + 4), Texture2D.whiteTexture);

        // Health Fill
        float fill = health.HealthPercentage;
        Color barColor = Color.Lerp(new Color(0.9f, 0.2f, 0.2f), new Color(0.2f, 0.85f, 0.3f), fill);
        GUI.color = barColor;
        GUI.DrawTexture(new Rect(x, y, barWidth * fill, barHeight), Texture2D.whiteTexture);

        // Health Label
        GUI.color = Color.white;
        GUIStyle textStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        GUI.Label(new Rect(x, y, barWidth, barHeight), $"HP: {Mathf.CeilToInt(health.CurrentHealth)} / {health.MaxHealth}", textStyle);
    }

    private void DrawGameOverScreen()
    {
        // Dark crimson backdrop
        Color prevBg = GUI.backgroundColor;
        Color prevColor = GUI.color;

        GUI.color = new Color(0.12f, 0.02f, 0.02f, 0.88f);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);

        int panelWidth = 440;
        int panelHeight = 360;
        float x = (Screen.width - panelWidth) * 0.5f;
        float y = (Screen.height - panelHeight) * 0.5f;

        // Modal Box
        GUI.color = new Color(0.1f, 0.1f, 0.12f, 0.95f);
        GUI.Box(new Rect(x, y, panelWidth, panelHeight), GUIContent.none);
        GUI.color = prevColor;

        GUILayout.BeginArea(new Rect(x + 24, y + 20, panelWidth - 48, panelHeight - 40));

        // Title
        GUIStyle titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 26,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(1f, 0.25f, 0.25f) }
        };
        GUILayout.Label("GAME OVER", titleStyle);
        GUILayout.Space(6);

        GUIStyle subStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(0.85f, 0.85f, 0.85f) }
        };
        GUILayout.Label("You ran out of health and were defeated!", subStyle);
        GUILayout.Space(14);

        // Stats Box
        GUIStyle statStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.yellow }
        };
        GUILayout.Label($"<b>Enemies Defeated:</b> {EnemySpawner.TotalEnemiesKilled}", statStyle);
        GUILayout.Label($"<b>Time Survived:</b> {timeSurvived:F1}s", statStyle);
        GUILayout.Space(20);

        // Action Buttons
        GUI.backgroundColor = new Color(0.2f, 0.8f, 0.35f);
        if (GUILayout.Button("PLAY AGAIN  (Restart)", GUILayout.Height(44)))
        {
            RestartGame();
        }
        GUI.backgroundColor = prevBg;

        GUILayout.Space(8);

        // Return to Lobby button
        if (NetworkManager.Instance != null)
        {
            GUI.backgroundColor = new Color(0.95f, 0.7f, 0.25f);
            if (GUILayout.Button("RETURN TO LOBBY", GUILayout.Height(38)))
            {
                IsGameOver = false;
                NetworkManager.Instance.LeaveRoom();
            }
            GUI.backgroundColor = prevBg;

            GUILayout.Space(8);
        }

        // Exit Game button
        GUI.backgroundColor = new Color(0.9f, 0.3f, 0.3f);
        if (GUILayout.Button("EXIT GAME", GUILayout.Height(36)))
        {
            if (NetworkManager.Instance != null)
            {
                NetworkManager.Instance.ExitGame();
            }
            else
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            }
        }
        GUI.backgroundColor = prevBg;

        GUILayout.EndArea();
    }
}
