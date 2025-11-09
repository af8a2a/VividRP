//Extra from NRD internal
#ifndef TIMER_H
#define TIMER_H
#include <cstdint>


class Timer {
public:
    Timer();

    double GetTimeStamp();
    void UpdateElapsedTimeSinceLastSave();
    void SaveCurrentTime();

    // In milliseconds
    inline float GetElapsedTime() {
        return m_Delta;
    }

    inline float GetSmoothedElapsedTime() {
        return m_SmoothedDelta;
    }

    inline float GetVerySmoothedElapsedTime() {
        return m_VerySmoothedDelta;
    }

private:
private:
    std::uint64_t m_Time = 0;
    double m_InvTicksPerMs = 0.0;
    float m_Delta = 1.0f;
    float m_SmoothedDelta = 1.0f;
    float m_VerySmoothedDelta = 1.0f;
};



#endif //TIMER_H
