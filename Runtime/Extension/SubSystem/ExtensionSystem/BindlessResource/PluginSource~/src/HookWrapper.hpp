//
// Created by 11252 on 2025/11/5.
//

#ifndef UNITYPLUGIN_HOOKWRAPPER_HPP
#define UNITYPLUGIN_HOOKWRAPPER_HPP

#include <MinHook.h>
#include <IUnityLog.h>

extern IUnityLog *g_Log;

template<typename T>
class HookWrapper {
public:
    explicit HookWrapper(LPVOID pTarget) : m_pTarget(pTarget), m_pOriginal(nullptr) {
    }

    void CreateAndEnable(T pDetour) {
        if (MH_CreateHook(
                m_pTarget,
                reinterpret_cast<LPVOID>(pDetour),
                reinterpret_cast<LPVOID *>(&m_pOriginal)) == MH_OK) {
            if (MH_EnableHook(m_pTarget) == MH_OK) {
                UNITY_LOG(g_Log, "MH_EnableHook success");
            } else {
                UNITY_LOG_ERROR(g_Log, "MH_EnableHook failure");
            }
        } else {
            UNITY_LOG_ERROR(g_Log, "MH_CreateHook failure");
        }
    }

    void Disable() const {
        if (MH_DisableHook(m_pTarget) == MH_OK) {
            UNITY_LOG(g_Log, "MH_DisableHook success");
        } else {
            UNITY_LOG_ERROR(g_Log, "MH_DisableHook failure");
        }
    }

    T GetOriginalPtr() const {
        return m_pOriginal;
    }

private:
    LPVOID m_pTarget;
    T m_pOriginal;
};


#endif //UNITYPLUGIN_HOOKWRAPPER_HPP
