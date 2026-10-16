using System;
using UnityEngine;
#if PHOTON_UNITY_NETWORKING
using Photon.Pun;
#endif

public interface IDamageable
{
    void TakeDamage(float amount, Vector3 hitPoint = default, Vector3 hitNormal = default);
}

/// <summary>
/// Universal health component for players and enemies.
/// Supports damage callbacks, healing, death, and Photon RPC synchronization.
/// </summary>
public class Health : MonoBehaviour, IDamageable
#if PHOTON_UNITY_NETWORKING
    , IPunObservable
#endif
{
    [Header("Health Settings")]
    [SerializeField] private float maxHealth = 100f;
    [SerializeField] private float currentHealth;
    [SerializeField] private bool destroyOnDeath = false;
    [SerializeField] private float deathDelay = 0f;

    [Header("Audio & Effects")]
    [SerializeField] private GameObject hitEffectPrefab;
    [SerializeField] private GameObject deathEffectPrefab;

    public event Action<float, float> OnHealthChanged; // current, max
    public event Action<float, Vector3, Vector3> OnDamaged; // amount, hitPoint, hitNormal
    public event Action OnDeath;

    public float CurrentHealth => currentHealth;
    public float MaxHealth => maxHealth;
    public bool IsDead => currentHealth <= 0f;
    public float HealthPercentage => Mathf.Clamp01(currentHealth / Mathf.Max(1f, maxHealth));

#if PHOTON_UNITY_NETWORKING
    private PhotonView photonView;
#endif

    private void Awake()
    {
#if PHOTON_UNITY_NETWORKING
        photonView = GetComponent<PhotonView>();
#endif
        currentHealth = maxHealth;
    }

    public void TakeDamage(float amount, Vector3 hitPoint = default, Vector3 hitNormal = default)
    {
        if (IsDead || amount <= 0f) return;

#if PHOTON_UNITY_NETWORKING
        if (PhotonNetwork.InRoom && photonView != null)
        {
            photonView.RPC(nameof(RPC_TakeDamage), RpcTarget.All, amount, hitPoint, hitNormal);
            return;
        }
#endif
        ApplyDamageLocally(amount, hitPoint, hitNormal);
    }

#if PHOTON_UNITY_NETWORKING
    [PunRPC]
    private void RPC_TakeDamage(float amount, Vector3 hitPoint, Vector3 hitNormal)
    {
        ApplyDamageLocally(amount, hitPoint, hitNormal);
    }
#endif

    private void ApplyDamageLocally(float amount, Vector3 hitPoint, Vector3 hitNormal)
    {
        if (IsDead) return;

        currentHealth = Mathf.Max(0f, currentHealth - amount);
        OnDamaged?.Invoke(amount, hitPoint, hitNormal);
        OnHealthChanged?.Invoke(currentHealth, maxHealth);

        if (hitEffectPrefab != null && hitPoint != Vector3.zero)
        {
            Quaternion rot = hitNormal != Vector3.zero ? Quaternion.LookRotation(hitNormal) : Quaternion.identity;
            Instantiate(hitEffectPrefab, hitPoint, rot);
        }

        if (currentHealth <= 0f)
        {
            Die();
        }
    }

    public void Heal(float amount)
    {
        if (IsDead || amount <= 0f) return;

        currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
    }

    private void Die()
    {
        OnDeath?.Invoke();

        if (deathEffectPrefab != null)
        {
            Instantiate(deathEffectPrefab, transform.position + Vector3.up, Quaternion.identity);
        }

        if (destroyOnDeath)
        {
#if PHOTON_UNITY_NETWORKING
            if (PhotonNetwork.InRoom && photonView != null && photonView.IsMine)
            {
                PhotonNetwork.Destroy(gameObject);
                return;
            }
#endif
            Destroy(gameObject, deathDelay);
        }
    }

    public void ResetHealth()
    {
        currentHealth = maxHealth;
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
    }

#if PHOTON_UNITY_NETWORKING
    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            stream.SendNext(currentHealth);
        }
        else
        {
            float netHealth = (float)stream.ReceiveNext();
            if (Mathf.Abs(currentHealth - netHealth) > 0.01f)
            {
                currentHealth = netHealth;
                OnHealthChanged?.Invoke(currentHealth, maxHealth);
            }
        }
    }
#endif
}
