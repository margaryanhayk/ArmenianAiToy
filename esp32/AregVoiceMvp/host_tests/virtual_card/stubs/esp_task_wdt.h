// Host stand-in: counts how often the firmware feeds the task watchdog.
#pragma once
inline int g_wdt_feeds = 0;
inline int esp_task_wdt_reset() { g_wdt_feeds++; return 0; }
