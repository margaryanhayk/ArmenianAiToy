// Host stand-in for the Arduino core: just enough for the firmware's own
// pure-logic files and its on-device self-test suites to run on a PC. No
// hardware, no network, no timers: time only moves when delay() is called.
// Used only by host_tests/virtual_card/. See README.md there.
#pragma once

#include <stdint.h>
#include <stddef.h>
#include <stdio.h>
#include <stdarg.h>
#include <string.h>
#include <stdlib.h>
#include <math.h>
#include <string>
#include <algorithm>

// ArduinoJson reads these before it is included. Stream input and String
// output are what the firmware uses; Print is not needed.
#define ARDUINOJSON_ENABLE_ARDUINO_STRING 1
#define ARDUINOJSON_ENABLE_ARDUINO_STREAM 1
#define ARDUINOJSON_ENABLE_ARDUINO_PRINT 0

#ifdef _MSC_VER
#define strcasecmp _stricmp
#define strncasecmp _strnicmp
#endif
#ifndef PROGMEM
#define PROGMEM
#endif
#define F(x) x

// Everything the firmware prints is also kept here so a test can assert on it.
struct HostSerial {
    std::string captured;
    bool echo = true;
    void emit(const char *s) {
        captured += s;
        if (echo) fputs(s, stdout);
    }
    int printf(const char *fmt, ...) {
        char buf[1024];
        va_list ap;
        va_start(ap, fmt);
        int n = vsnprintf(buf, sizeof(buf), fmt, ap);
        va_end(ap);
        emit(buf);
        return n;
    }
    void println(const char *s = "") { emit(s); emit("\n"); }
    void println(int v) { printf("%d\n", v); }
    void print(const char *s) { emit(s); }
    void flush() { fflush(stdout); }
    bool saw(const char *needle) const { return captured.find(needle) != std::string::npos; }
    void clear() { captured.clear(); }
};

struct HostESP {
    uint32_t getFreeHeap() { return 90000; }
    uint32_t getFreePsram() { return 7800000; }
};

inline HostSerial Serial;
inline HostESP ESP;

// Time is a variable. delay() advances it, so retry gaps and "wait 5 s" memos
// behave as they would on the toy without the test actually sleeping.
inline uint32_t g_host_millis = 30000;
inline uint32_t millis() { return g_host_millis; }
inline void delay(uint32_t ms) { g_host_millis += ms; }

// Minimal Arduino String (enough for ArduinoJson and the self-test suites).
class String {
    std::string s_;
public:
    String() {}
    String(const char *c) : s_(c ? c : "") {}
    String(int v) : s_(std::to_string(v)) {}
    String(unsigned v) : s_(std::to_string(v)) {}
    String(long v) : s_(std::to_string(v)) {}
    String(unsigned long v) : s_(std::to_string(v)) {}
    const char *c_str() const { return s_.c_str(); }
    unsigned length() const { return (unsigned)s_.size(); }
    bool reserve(unsigned n) { s_.reserve(n); return true; }
    bool concat(const char *c, unsigned n) { s_.append(c, n); return true; }
    bool concat(const char *c) { s_ += c; return true; }
    bool concat(char c) { s_ += c; return true; }
    int indexOf(const char *x) const { auto p = s_.find(x); return p == std::string::npos ? -1 : (int)p; }
    int indexOf(const String &x) const { return indexOf(x.c_str()); }
    int indexOf(char c) const { auto p = s_.find(c); return p == std::string::npos ? -1 : (int)p; }
    bool startsWith(const char *x) const { return s_.rfind(x, 0) == 0; }
    String &operator+=(const char *c) { s_ += c; return *this; }
    String &operator+=(const String &o) { s_ += o.s_; return *this; }
    bool operator==(const String &o) const { return s_ == o.s_; }
    bool operator==(const char *c) const { return s_ == c; }
    friend String operator+(const String &a, const String &b) { String r; r.s_ = a.s_ + b.s_; return r; }
    friend String operator+(const char *a, const String &b) { String r; r.s_ = std::string(a) + b.s_; return r; }
    friend String operator+(const String &a, const char *b) { String r; r.s_ = a.s_ + b; return r; }
};

class Print {
public:
    virtual ~Print() {}
    virtual size_t write(uint8_t) { return 0; }
    virtual size_t write(const uint8_t *b, size_t n) { size_t k = 0; for (size_t i = 0; i < n; i++) k += write(b[i]); return k; }
};

class Printable {
public:
    virtual ~Printable() {}
    virtual size_t printTo(Print &p) const = 0;
};

class Stream : public Print {
public:
    virtual int available() = 0;
    virtual int read() = 0;
    virtual int peek() = 0;
    virtual size_t readBytes(char *buf, size_t n) = 0;
};
