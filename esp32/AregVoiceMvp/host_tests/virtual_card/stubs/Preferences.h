// In-memory NVS for host_tests/virtual_card/. The store is global, so a
// "power cycle" in a test simply keeps it, exactly as flash would.
#pragma once

#include <Arduino.h>
#include <map>
#include <string>

inline std::map<std::string, std::string> g_nvs;

class Preferences {
    std::string ns_;
    bool open_ = false, ro_ = false;
    std::string k(const char *key) const { return ns_ + "/" + key; }
public:
    bool begin(const char *name, bool ro = false) { ns_ = name; ro_ = ro; open_ = true; return true; }
    void end() { open_ = false; }
    size_t putString(const char *key, const char *v) { if (ro_) return 0; g_nvs[k(key)] = v; return strlen(v); }
    size_t getString(const char *key, char *out, size_t max) {
        auto it = g_nvs.find(k(key));
        if (it == g_nvs.end() || max == 0) { if (max) out[0] = 0; return 0; }
        size_t n = std::min(max - 1, it->second.size());
        memcpy(out, it->second.data(), n);
        out[n] = 0;
        return n;
    }
    String getString(const char *key, String def = String()) {
        auto it = g_nvs.find(k(key));
        return it == g_nvs.end() ? def : String(it->second.c_str());
    }
    size_t putBytes(const char *key, const void *v, size_t len) {
        if (ro_) return 0;
        g_nvs[k(key)] = std::string((const char *)v, len);
        return len;
    }
    size_t getBytes(const char *key, void *out, size_t max) {
        auto it = g_nvs.find(k(key));
        if (it == g_nvs.end()) return 0;
        size_t n = std::min(max, it->second.size());
        memcpy(out, it->second.data(), n);
        return n;
    }
    size_t getBytesLength(const char *key) { auto it = g_nvs.find(k(key)); return it == g_nvs.end() ? 0 : it->second.size(); }
    uint32_t getUInt(const char *key, uint32_t def = 0) { auto it = g_nvs.find(k(key)); return it == g_nvs.end() ? def : (uint32_t)strtoul(it->second.c_str(), 0, 10); }
    size_t putUInt(const char *key, uint32_t v) { if (ro_) return 0; g_nvs[k(key)] = std::to_string(v); return 4; }
    uint8_t getUChar(const char *key, uint8_t def = 0) { return (uint8_t)getUInt(key, def); }
    size_t putUChar(const char *key, uint8_t v) { return putUInt(key, v); }
    bool getBool(const char *key, bool def = false) { return getUInt(key, def ? 1 : 0) != 0; }
    size_t putBool(const char *key, bool v) { return putUInt(key, v ? 1 : 0); }
    bool isKey(const char *key) { return g_nvs.count(k(key)) != 0; }
    bool remove(const char *key) { return g_nvs.erase(k(key)) != 0; }
};
