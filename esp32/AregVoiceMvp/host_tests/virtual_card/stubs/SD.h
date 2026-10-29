// In-memory SD card with fault injection, for host_tests/virtual_card/.
// Only the calls the firmware's index and story-selection code makes.
#pragma once

#include <Arduino.h>
#include <map>
#include <memory>
#include <string>

#define FILE_READ "r"
#define FILE_WRITE "w"

struct HostFileData {
    std::string bytes;
    long size_override = -1;          // report this size without holding the bytes
    size_t fail_after = (size_t)-1;   // reads stop here for good: a dying card
    int short_reads_left = 0;         // the next N opens read only half: a flaky card
    int opens = 0;                    // how many times THIS file was opened
};

class File : public Stream {
    std::shared_ptr<HostFileData> d_;
    size_t pos_ = 0;
    size_t limit_ = (size_t)-1;       // per-open cap (short_reads_left)
    bool open_ = false;
    size_t lim() const {
        size_t l = d_ ? d_->bytes.size() : 0;
        if (d_ && d_->fail_after < l) l = d_->fail_after;
        if (limit_ < l) l = limit_;
        return l;
    }
public:
    File() {}
    File(std::shared_ptr<HostFileData> d, size_t limit) : d_(d), limit_(limit), open_(true) {}
    operator bool() const { return open_; }
    size_t size() const { return !d_ ? 0 : (d_->size_override >= 0 ? (size_t)d_->size_override : d_->bytes.size()); }
    void close() { open_ = false; }
    int available() override { return open_ && pos_ < lim() ? (int)(lim() - pos_) : 0; }
    int read() override { char c; return readBytes(&c, 1) ? (unsigned char)c : -1; }
    int peek() override { return available() ? (unsigned char)d_->bytes[pos_] : -1; }
    size_t readBytes(char *buf, size_t n) override {
        if (!open_ || !d_ || pos_ >= lim()) return 0;
        size_t k = std::min(n, lim() - pos_);
        memcpy(buf, d_->bytes.data() + pos_, k);
        pos_ += k;
        return k;
    }
};

class HostSD {
public:
    std::map<std::string, std::shared_ptr<HostFileData>> files;
    int opens = 0;                    // how many times anything opened a file
    bool exists(const char *p) { return files.count(p) != 0; }
    File open(const char *p, const char * = FILE_READ) {
        opens++;
        auto it = files.find(p);
        if (it == files.end()) return File();
        it->second->opens++;
        size_t limit = (size_t)-1;
        if (it->second->short_reads_left > 0) {
            it->second->short_reads_left--;
            limit = it->second->bytes.size() / 2;
        }
        return File(it->second, limit);
    }
    std::shared_ptr<HostFileData> put(const std::string &path, const std::string &data) {
        auto d = std::make_shared<HostFileData>(); d->bytes = data; files[path] = d; return d;
    }
    std::shared_ptr<HostFileData> put_sized(const std::string &path, long size) {
        auto d = std::make_shared<HostFileData>(); d->size_override = size; files[path] = d; return d;
    }
    bool remove(const char *p) { return files.erase(p) != 0; }
    bool rename(const char *a, const char *b) {
        auto it = files.find(a);
        if (it == files.end() || files.count(b)) return false;   // FAT refuses an existing destination
        files[b] = it->second;
        files.erase(a);
        return true;
    }
};

inline HostSD SD;
