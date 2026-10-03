# PRD — WinNotch

**Working Name:** WinNotch  
**Versi PRD:** 0.1  
**Platform awal:** Windows 11  
**Tanggal:** 3 Oktober 2026  
**Tipe aplikasi:** Native desktop utility  
**Konsep:** MacBook-style virtual notch + Dynamic-Island-style interaction

---

## 1. Product Vision

WinNotch adalah aplikasi Windows yang membuat sebuah **virtual notch interaktif di tengah atas layar laptop**.

Dalam keadaan diam, notch terlihat seperti perpanjangan bezel hitam laptop. Saat terjadi aktivitas seperti musik berganti, volume berubah, baterai di-charge, timer selesai, atau notifikasi masuk, notch akan **berubah ukuran secara halus** dan menampilkan informasi yang relevan.

Pengguna juga dapat mengklik notch untuk membuka tampilan lebih besar yang berisi kontrol.

Prinsip utamanya:

> **Always available, rarely distracting.**

Notch harus terasa seperti bagian dari laptop, bukan seperti jendela aplikasi yang ditempelkan di atas desktop.

---

# 2. Masalah yang Ingin Diselesaikan

Windows memiliki informasi yang tersebar di banyak tempat:

- media control,
- notification center,
- battery tray,
- volume flyout,
- timer,
- quick settings.

WinNotch menyatukan informasi yang sedang relevan ke satu area kecil di atas layar.

Contohnya:

**Spotify berganti lagu**

Notch:

```text
┌─────────────────────────────┐
│  🎵  The Weeknd — Starboy  │
└─────────────────────────────┘
```

beberapa detik kemudian kembali menjadi:

```text
        ┌──────────────┐
        │              │
        └──────────────┘
```

Ketika diklik:

```text
┌────────────────────────────────────┐
│  [Album]   Starboy                 │
│            The Weeknd              │
│                                    │
│       ◀       ▶/❚❚       ▶        │
│                                    │
│ ━━━━━━━━━━━●━━━━━━━━━━━━━━         │
└────────────────────────────────────┘
```

---

# 3. Design Philosophy

Kita **tidak akan menyalin Dynamic Island pixel-for-pixel**.

Yang kita ambil adalah pola interaksinya:

**Idle → Peek → Compact Live → Expanded → Collapse**

Apple merekomendasikan informasi Live Activity yang mudah dipahami sekilas dan membatasi aksi pada fungsi langsung yang relevan. :chatgpt-content-reference{index="3"}

Desain visualnya justru dibuat lebih cocok untuk Windows.

### Identitas WinNotch

MacBook:

```text
screen top
────────┐       ┌────────
        │ notch │
        └───────┘
```

iPhone:

```text
       ╭────────────╮
       │   island   │
       ╰────────────╯
```

WinNotch:

```text
screen top
────────╮██████████╭────────
        ╰██████████╯
```

Jadi notch **menempel ke atas**, bukan mengambang.

Ketika aktif:

```text
────────╮████████████████████╭────────
        │                     │
        ╰─────────────────────╯
```

Ia tumbuh ke bawah dan ke samping.

---

# 4. Target User

Versi pertama difokuskan untuk:

- laptop Windows 11,
- layar 1080p sampai 4K,
- single monitor maupun multi-monitor,
- pengguna Spotify/YouTube/Browser/media player,
- pengguna yang ingin desktop lebih estetik sekaligus fungsional.

Tidak dibutuhkan:

- akun,
- server,
- internet,
- cloud backend.

Semua proses sebisa mungkin berjalan lokal.

---

# 5. Core UX States

| State | Ukuran awal yang diusulkan | Fungsi |
|---|---:|---|
| Hidden | 0 × 0 | fullscreen/game |
| Idle | 126 × 30 DIP | notch dekoratif |
| Peek | 180–280 × 38 DIP | event singkat |
| Compact Live | 220–300 × 42 DIP | media/timer |
| Expanded | ±380 × 160 DIP | interactive panel |
| Large Expanded | max ±440 × 240 DIP | notification/timer detail |

Ukuran menggunakan **DIP**, bukan pixel mentah, sehingga tetap proporsional pada display scaling 100%, 125%, 150%, dan sebagainya. Windows menyediakan API DPI per-window untuk kebutuhan tersebut. :chatgpt-content-reference{index="4"}

---

# 6. Idle State

Idle adalah state default.

### Visual

Lebar:

**126 DIP**

Tinggi:

**30 DIP**

Posisi:

```text
X = screenCenter - width / 2
Y = 0
```

Sehingga notch benar-benar menyatu dengan tepi atas layar.

### Bentuk

```text
Top-left radius     = 0
Top-right radius    = 0

Bottom-left radius  = 15
Bottom-right radius = 15
```

Warna utama:

`#050505`

atau true black:

`#000000`

Pengguna nantinya bisa memilih.

---

# 7. Hover Behaviour

Windows memiliki mouse, sehingga kita bisa melakukan sesuatu yang Dynamic Island iPhone tidak punya.

Saat mouse memasuki notch:

```text
126 × 30
    ↓
138 × 32
```

dengan animasi sangat kecil.

Tujuannya hanya memberikan feedback:

> "Area ini bisa diklik."

**Jangan langsung membuka panel hanya karena hover.**

Kalau expanded muncul setiap mouse lewat bagian atas layar, aplikasi akan cepat terasa mengganggu.

---

# 8. Click Behaviour

Single click:

```text
IDLE
 ↓
EXPANDED
```

Click outside:

```text
EXPANDED
 ↓
IDLE
```

`Esc`:

```text
EXPANDED
 ↓
IDLE
```

Right click:

```text
Settings
Pause WinNotch
Disable for this app
Restart
Exit
```

---

# 9. Animation System

Animasi merupakan komponen penting karena ilusi “satu objek yang berubah bentuk” jauh lebih bagus daripada membuka window baru.

Windows Composition mendukung spring animations untuk efek seperti ini. :chatgpt-content-reference{index="5"}

Target motion:

| Animation | Durasi |
|---|---:|
| Idle → Peek | 180–220 ms |
| Idle → Expanded | 240–300 ms |
| Expanded → Idle | 180–220 ms |
| Content fade | 100–140 ms |
| Notification dismiss | 180 ms |

Gunakan:

**Spring / ease-out**

bukan linear animation.

Contohnya:

```text
126
 ↓
170
 ↓
260
 ↓
250
```

sedikit overshoot lalu settle.

Windows juga menyediakan setting untuk mengetahui apakah pengguna mematikan client-area animation, jadi WinNotch harus menyediakan **Reduced Motion**. :chatgpt-content-reference{index="6"}

---

# 10. Typography & Iconography

Font:

**Segoe UI Variable**

karena native Windows.

Ukuran:

| Elemen | Size |
|---|---:|
| secondary info | 11 |
| normal | 12 |
| title | 14 |
| expanded title | 15–16 |

Icon:

**Fluent System Icons**

bukan SF Symbols.

Dengan begitu aplikasinya terinspirasi Dynamic Island, tetapi tetap mempunyai identitas Windows.

---

# 11. Feature Priorities

## P0 — MVP

### Notch Shell

- top-center overlay,
- always on top,
- borderless,
- tidak muncul di taskbar,
- tidak muncul normal di Alt+Tab,
- adaptive DPI,
- adaptive monitor,
- idle / compact / expanded,
- click interaction,
- auto collapse.

Windows memiliki `IsAlwaysOnTop` dan Win32 `HWND_TOPMOST` untuk menjaga overlay berada di atas window biasa. :chatgpt-content-reference{index="7"}

Window utility juga bisa menggunakan karakteristik tool window agar tidak memenuhi Alt+Tab seperti aplikasi biasa. :chatgpt-content-reference{index="8"}

---

## P0 — Media Player Integration

Saat Spotify, YouTube Music, browser, VLC, dan aplikasi kompatibel sedang memutar media:

```text
╭──────────────────────────────╮
│ 🎵 Starboy       ▶/❚❚       │
╰──────────────────────────────╯
```

Expanded:

```text
┌──────────────────────────────────┐
│ [art]  Starboy                   │
│        The Weeknd                │
│                                  │
│       ◀    ▶/❚❚    ▶            │
│                                  │
│ ━━━━━━━━━━━●━━━━━━━━━━━          │
└──────────────────────────────────┘
```

Requirement:

- album artwork,
- song title,
- artist,
- playback state,
- previous,
- play/pause,
- next.

Windows menyediakan `GlobalSystemMediaTransportControlsSessionManager` untuk membaca sesi media sistem; API ini membutuhkan capability `globalMediaControl`. :chatgpt-content-reference{index="9"}

Ini harus menjadi **fitur utama MVP**, karena integrasinya kuat dan langsung membuat notch terasa hidup.

---

# 12. Battery & Charging Event

Saat charger dipasang:

```text
╭──────────────────────╮
│ ⚡ Charging  67%     │
╰──────────────────────╯
```

Lalu setelah ±3 detik:

```text
╭──────────────╮
│              │
╰──────────────╯
```

Event:

```text
charger connected
charger disconnected
battery low
battery full
battery saver
```

Battery Saver juga dapat dipantau melalui Windows power APIs. :chatgpt-content-reference{index="10"}

---

# 13. Volume Indicator

Saat volume berubah:

```text
╭─────────────────────────────╮
│ 🔊  ███████████░░░   72%   │
╰─────────────────────────────╯
```

Mute:

```text
╭───────────────────╮
│ 🔇  Muted         │
╰───────────────────╯
```

Targetnya nanti bisa menggantikan kebutuhan pengguna memperhatikan volume flyout Windows.

---

# 14. Notification Integration

Ini sebaiknya masuk **P1 setelah shell stabil**.

Contoh:

WhatsApp:

```text
╭──────────────────────────────────╮
│ WhatsApp                         │
│ Budi                             │
│ Udah sampai mana?                │
╰──────────────────────────────────╯
```

Windows sekarang menyediakan Notification Listener yang dapat membaca notifikasi dari aplikasi lain, tetapi aplikasinya harus meminta izin pengguna dan mendeklarasikan `userNotificationListener`. :chatgpt-content-reference{index="11"}

Saat onboarding:

```text
Allow WinNotch to read Windows notifications?

[ Enable Notifications ]

WinNotch processes notification content locally.
```

Jika user memilih deny:

**aplikasi tetap berjalan.**

Media, battery dan fitur lain tidak bergantung pada izin tersebut.

### Penting

WinNotch **tidak boleh otomatis menghapus notifikasi asli Windows**.

Walaupun API memungkinkan aplikasi menghapus notification tertentu, Microsoft memperingatkan agar operasi tersebut digunakan hati-hati. :chatgpt-content-reference{index="12"}

Default behaviour:

```text
Windows notification → tetap ada
WinNotch → mirror/preview
```

---

# 15. Timer

Built-in timer akan membuat notch lebih berguna tanpa membutuhkan integrasi aplikasi eksternal.

Expanded:

```text
┌────────────────────────────┐
│ Timer                      │
│                            │
│          24:13             │
│                            │
│      Pause     Cancel      │
└────────────────────────────┘
```

Compact:

```text
╭───────────────────╮
│ ⏱ 24:13          │
╰───────────────────╯
```

Timer tetap compact sampai selesai.

---

# 16. Event Priority Engine

Notch tidak boleh berubah secara acak ketika beberapa event muncul bersamaan.

Kita memerlukan **Event Arbitration Engine**.

Prioritas awal:

```text
Critical system event
        ↓
Timer completion
        ↓
Notification
        ↓
Volume/Brightness
        ↓
Media change
        ↓
Persistent media
        ↓
Idle
```

Misalnya:

Spotify sedang tampil.

Kemudian charger masuk.

```text
MEDIA
 ↓
CHARGING: 72%
 ↓ 3 sec
MEDIA
```

Bukan:

```text
MEDIA → IDLE → CHARGING → IDLE → MEDIA
```

Dengan begitu animasinya terasa seperti satu sistem.

---

# 17. State Machine

Core architecture harus memakai explicit state machine:

```text
             ┌─────────┐
             │ HIDDEN  │
             └────┬────┘
                  │
                  ↓
┌──────────┐   ┌────────┐
│ EXPANDED │ ← │  IDLE  │
└────┬─────┘   └───┬────┘
     │             │
     │             ↓
     │        ┌─────────┐
     └──────→ │  PEEK   │
              └────┬────┘
                   │
                   ↓
              ┌─────────┐
              │ COMPACT │
              └─────────┘
```

State bukan ditentukan langsung oleh UI.

Yang menentukan adalah:

```text
NotchStateManager
```

---

# 18. Fullscreen Behaviour

Ini requirement yang sangat penting.

Bayangkan sedang:

- main game,
- menonton Netflix fullscreen,
- presentasi PowerPoint,
- menonton YouTube fullscreen.

Kita tidak mau:

```text
           ████████
        GAME SCREEN
```

masih ditutupi notch.

Windows memiliki notification state termasuk `QUNS_RUNNING_D3D_FULL_SCREEN`, `QUNS_BUSY`, dan presentation mode yang dapat digunakan untuk mendeteksi keadaan ini. :chatgpt-content-reference{index="13"}

Default behaviour:

```text
Fullscreen detected
      ↓
Notch collapse
      ↓
Hide
```

Keluar fullscreen:

```text
wait 500ms
 ↓
restore notch
```

Setting:

```text
Fullscreen behavior

● Hide completely
○ Show only critical events
○ Always show
```

---

# 19. Browser / Maximized App Problem

Ini salah satu masalah terbesar yang ditemukan dari sisi UX.

Karena notch adalah overlay, ia bisa menutupi bagian tengah:

```text
Chrome tabs
VS Code title bar
Explorer title bar
```

Kita **tidak akan mengurangi Windows WorkArea**, karena itu akan mendorong semua aplikasi ke bawah dan membuat UX lebih buruk.

Solusinya:

### App Exclusion

Settings:

```text
Hide notch in:

☐ Chrome
☐ Edge
☐ VS Code
☐ Games
```

### Auto Hide Mode

Pilihan:

```text
Notch Visibility

● Always visible
○ Show only when active
○ Smart hide
```

Smart hide menjadi default yang layak diuji.

---

# 20. Multi-Monitor Support

Windows App SDK memiliki `DisplayArea` untuk mendapatkan area monitor, primary display dan work area. :chatgpt-content-reference{index="14"}

Settings:

```text
Display

● Primary monitor
○ Monitor containing active window
○ Monitor 1
○ Monitor 2
```

Default MVP:

**Primary monitor.**

Versi berikut:

**Follow active monitor.**

---

# 21. Settings Panel

Settings bukan berada di dalam notch terus-menerus.

Right click → Settings membuka normal window.

Struktur:

```text
General
Appearance
Behavior
Modules
Notifications
Display
Performance
About
```

### General

```text
Start WinNotch with Windows       ON
Always on top                     ON
Hide in fullscreen                ON
Launch minimized                  ON
```

### Appearance

```text
Size             Small / Default / Large
Style            Attached / Floating
Black level      #000000
Animations       ON
Animation speed
```

### Modules

```text
Media            ON
Notifications    OFF
Battery          ON
Volume           ON
Timer            ON
Clock            OFF
```

---

# 22. Privacy Requirements

Aplikasi ini cukup sensitif karena suatu hari dapat membaca notifikasi.

Maka requirement-nya:

**Local first.**

Tidak ada:

```text
analytics notification content
cloud synchronization
notification text upload
media history upload
```

Log:

```text
event type
timestamp
error
module
```

Tidak:

```text
WhatsApp message contents
email contents
private media metadata history
```

Secara default.

---

# 23. Recommended Tech Stack

Setelah melihat requirement-nya, untuk versi pertama aku justru menyarankan:

### C# + .NET 10 + WPF

bukan Electron, React desktop, atau Python.

Microsoft saat ini menyediakan tooling WPF pada .NET 10, dan .NET 10 merupakan LTS aktif sampai November 2028. :chatgpt-content-reference{index="15"}

Arsitektur:

```text
.NET 10
│
├── WPF
│   └── Notch UI
│
├── Windows App SDK
│
├── WinRT APIs
│   ├── Media Sessions
│   ├── Notifications
│   └── Power
│
└── Win32 Interop
    ├── Window positioning
    ├── Z-order
    ├── Fullscreen detection
    ├── DPI
    └── Audio
```

Kenapa **WPF** untuk kasus ini?

Karena target kita bukan aplikasi bisnis biasa.

Targetnya adalah:

```text
transparent
frameless
irregular-shaped
always-on-top
animated
native Windows overlay
```

WPF sangat matang untuk custom transparent windows dan bisa digabung dengan Win32/WinRT.

Windows App SDK tetap bisa digunakan bersama framework desktop seperti WPF. Microsoft juga secara eksplisit menyediakan notification APIs untuk WPF dan WinForms. :chatgpt-content-reference{index="16"}

---

# 24. Windows App SDK Requirement

Untuk API Windows modern kita dapat memakai Windows App SDK stable.

Per 29 September 2026, halaman resmi Microsoft mencantumkan **Windows App SDK stable 2.5.1**, dirilis 16 September 2026. :chatgpt-content-reference{index="17"}

Jadi stack awal:

```text
Visual Studio 2026
.NET 10 LTS
C#
WPF
Windows App SDK 2.x stable
Windows SDK
```

Tidak perlu:

```text
Node.js
Python runtime
browser engine
server
database
```

---

# 25. Proposed Software Architecture

```text
WinNotch.exe
│
├── Core
│   ├── NotchStateManager
│   ├── EventBus
│   ├── EventPriorityManager
│   └── SettingsManager
│
├── Window
│   ├── OverlayWindow
│   ├── PositionService
│   ├── MonitorService
│   └── FullscreenService
│
├── Modules
│   ├── MediaModule
│   ├── AudioModule
│   ├── BatteryModule
│   ├── NotificationModule
│   └── TimerModule
│
├── UI
│   ├── IdleView
│   ├── MediaView
│   ├── VolumeView
│   ├── BatteryView
│   ├── NotificationView
│   └── ExpandedView
│
└── Native
    ├── Win32
    ├── WinRT
    └── WindowsAppSDK
```

Ini sengaja modular.

Misalnya nanti ingin menambah:

```text
BluetoothModule
WeatherModule
DownloadModule
CalendarModule
MicrophoneModule
```

core UI tidak perlu diubah besar.

---

# 26. Window Requirements

Notch window:

```text
Borderless              YES
Resizable manually      NO
Taskbar icon             NO
Alt+Tab entry            NO
Always on top            YES
Transparent background  YES
Position fixed           YES
DPI aware                YES
```

Pada state pasif, kita sebisa mungkin menghindari mengambil focus aplikasi aktif.

Windows menyediakan `WS_EX_NOACTIVATE`, tetapi ada implikasi accessibility sehingga pendekatan ini harus digunakan hanya pada passive overlay, bukan secara sembarangan pada UI expanded. :chatgpt-content-reference{index="18"}

Idealnya:

```text
Idle/Peek
→ does not steal focus

Expanded
→ interactive window
```

---

# 27. Notification Permission

Onboarding pertama:

```text
Welcome to WinNotch

Media Control
✓ Automatically available

Battery
✓ Automatically available

Notifications
Requires permission

[ Continue ]
```

Lalu baru meminta permission notification.

Jangan langsung meminta semua izin saat aplikasi pertama terbuka.

---

# 28. Non-Functional Requirements

Target internal MVP:

| Metric | Target |
|---|---:|
| Idle CPU | <0.5% typical |
| Idle RAM | ≤120 MB target |
| Startup | <2 sec |
| UI animation | 60 FPS |
| UI response | <100 ms |
| event → notch | <500 ms |
| crash-free session | >99% |
| internet dependency | none |

Aplikasi sebaiknya **event-driven**, bukan polling setiap 50 ms.

Tujuannya supaya baterai laptop tidak terkuras hanya karena notch.

---

# 29. Accessibility

Requirements:

- respect Reduce Motion,
- keyboard navigable expanded panel,
- meaningful automation labels,
- sufficient contrast,
- high-contrast fallback,
- Escape closes panel,
- font mengikuti Windows scaling.

Windows menyediakan sistem preference untuk memeriksa apakah client animation dimatikan pengguna. :chatgpt-content-reference{index="19"}

---

# 30. MVP Feature Set

Untuk **WinNotch v0.1**, menurutku jangan langsung memasukkan 20 fitur.

Scope terbaik:

```text
✓ Virtual notch
✓ Idle animation
✓ Hover
✓ Click expansion
✓ Media integration
✓ Play / pause
✓ Previous / next
✓ Album art
✓ Battery + charging
✓ Volume indicator
✓ Fullscreen auto-hide
✓ Multi-DPI support
✓ Startup with Windows
✓ Settings
✓ System tray
```

Lalu **v0.2**:

```text
Notifications
Timer
Brightness
Multi-monitor follow mode
Per-app exclusions
Themes
```

v0.3:

```text
Calendar
Downloads
Bluetooth
Custom modules
Plugin API
```

---

# 31. Acceptance Criteria MVP

MVP dianggap berhasil ketika:

1. Setelah Windows login, WinNotch bisa otomatis muncul di tengah atas layar.
2. Tidak muncul sebagai normal window di taskbar.
3. Tidak mengganggu focus saat idle.
4. Spotify/Browser media terdeteksi.
5. Judul lagu dan artist berubah otomatis.
6. Tombol play/pause/next/previous berfungsi.
7. Memasang charger menghasilkan charging animation.
8. Mengubah volume menghasilkan volume animation.
9. Klik notch membuka expanded panel.
10. Klik area luar atau `Esc` menutup panel.
11. Layar scaling 100%, 125%, 150%, dan 200% tidak merusak layout.
12. Saat fullscreen game/video, notch dapat otomatis menghilang.
13. Aplikasi berjalan tanpa administrator privileges.
14. Semua core functionality tetap bekerja tanpa internet.

Catatan penting: sebaiknya memang **jangan menjalankan aplikasi sebagai Administrator**; Windows App SDK juga memiliki batasan tertentu untuk notifications pada aplikasi elevated. :chatgpt-content-reference{index="20"}

---

# 32. Visual Direction Final

Aku akan memilih bentuk ini sebagai identitas utama:

### Idle

```text
             ██████████████
             ██████████████
              ╲██████████╱
```

### Media peek

```text
         █████████████████████████
         █ 🎵 Starboy       ▶  █
          ╲█████████████████████╱
```

### Expanded

```text
      ████████████████████████████████████
      █                                    █
      █  [art]   Starboy                   █
      █          The Weeknd                █
      █                                    █
      █         ◀    ❚❚    ▶              █
      █                                    █
       ╲__________________________________╱
```

Yang menarik adalah:

**bagian atas tidak pernah mempunyai rounded corner.**

Karena secara visual WinNotch berasal dari bezel atas laptop.

Itu yang membuatnya berbeda dari sekadar clone Dynamic Island.

---

# 33. Recommended Interaction Model

Kalau kita susun keseluruhan UX:

```text
Laptop menyala
       ↓
small black notch
       ↓

Spotify play
       ↓
notch melebar
       ↓
🎵 song title
       ↓
3 detik
       ↓
compact media notch

user hover
       ↓
subtle grow

user click
       ↓
expanded player

user click outside
       ↓
compact

charger connected
       ↓
⚡ Charging 78%
       ↓
media kembali

YouTube fullscreen
       ↓
notch menghilang

keluar fullscreen
       ↓
notch kembali
```

Ini menurutku adalah flow yang membuat aplikasinya terasa **seperti fitur OS**, bukan floating widget.

---

# 34. Risiko Teknis Utama

Ada empat hal yang perlu kita uji paling awal.

**Pertama, window focus.** Notch tidak boleh mencuri focus saat kamu sedang mengetik.

**Kedua, browser chrome.** Posisi top-center bisa menutupi tab browser, jadi smart-hide/per-app exclusion wajib dipikirkan sejak awal.

**Ketiga, notification duplication.** Jika WinNotch menampilkan toast sementara Windows juga menampilkan toast, bisa terasa berlebihan. Karena itu notification module sebaiknya datang setelah core UX stabil.

**Keempat, fullscreen/game.** Topmost overlay yang salah implementasi dapat muncul di atas game. Kita sudah punya API Windows untuk mendeteksi keadaan fullscreen/presentation, jadi ini perlu masuk core architecture, bukan ditambal belakangan. :chatgpt-content-reference{index="21"}

---

# 35. Final Product Definition

Jadi secara ringkas, produk yang sebaiknya kita bangun adalah:

> **WinNotch adalah lightweight native Windows utility yang menambahkan virtual notch di bagian tengah atas display. Notch berfungsi sebagai ambient interaction surface untuk media, system events, battery, volume, timer, dan notification, dengan state-based morphing UI yang otomatis menyesuaikan konteks pengguna.**

Dan arsitektur yang aku pilih untuk proyek ini adalah:

```text
WinNotch
C#
.NET 10 LTS
WPF
Windows App SDK
WinRT
Win32 Interop

MVP:
Notch Shell
+ Media
+ Battery
+ Volume
+ Fullscreen Detection
+ Settings
```

Menurutku **jangan mulai dari notification dulu**. Tahap pertama justru sebaiknya membuat **shell notch + animasi + Spotify/media integration** sampai benar-benar terasa halus. Kalau fondasi itu sudah bagus, battery, volume, notification, timer dan fitur lainnya hanya menjadi module yang mengirim event ke `NotchStateManager`.

Tahap implementasi pertama nantinya bisa kita buat sebagai **prototype nyata WinNotch v0.1**, dimulai dari struktur project C# WPF, transparent topmost notch, state machine `Idle → Peek → Expanded`, lalu integrasi media Windows.