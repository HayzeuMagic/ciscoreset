# Cisco Reset

Cisco Reset is a small Windows app that resets a lab PC's Cisco devices at the end of a class with one click. It does the same thing you would type in SecureCRT, on each device in turn:

```
pass
en
pass
wr er
delete vlan.dat
reload
```

It works on the three devices that belong to the PC it runs on:

| Step | Device | Address | What it is |
|---|---|---|---|
| 1 | **CUCM** | `10.m.100.8` | Call manager (CallManager Express on a Cisco router) |
| 2 | **coreTAAS** | `10.m.1.2` | Backup core switch |
| 3 | **coreBABA** | `10.m.1.4` | Main core switch |

**`m` is the PC number**: the last 2 digits of the PC's Wi-Fi IP address. On a PC with Wi-Fi IP `10.3.3.112`, `m` is `12`, so the app resets `10.12.100.8`, `10.12.1.2` and `10.12.1.4`. The app works it out by itself.

You don't need SecureCRT to use it. SecureCRT only keeps shortcuts to the devices. The configuration lives on the devices, and Cisco Reset talks to them over Telnet directly. Your SecureCRT sessions are left as they are.

---

## Contents

- [Part 1: Build Cisco Reset (once)](#part-1-build-cisco-reset-once)
- [Part 2: Put it on every lab PC](#part-2-put-it-on-every-lab-pc)
- [Part 3: How to use Cisco Reset, step by step](#part-3-how-to-use-cisco-reset-step-by-step)
- [What the tile colours mean](#what-the-tile-colours-mean)
- [Troubleshooting](#troubleshooting)
- [Safety rules (built in)](#safety-rules-built-in)
- [Settings](#settings)
- [What's in the folder](#whats-in-the-folder)
- [Exactly what it types](#exactly-what-it-types)

---

## Part 1: Build Cisco Reset (once)

**What you need:** Windows 10 or 11. .NET Framework 4.8 is already built in, so there's nothing to download.

1. Get the files: **Download ZIP** from the green **`<> Code`** button on GitHub and extract it, or clone the repository with Git.
2. Double-click **`build.bat`**.
3. When it says **`Built CiscoReset.exe successfully.`**, press any key.

You now have **`CiscoReset.exe`** in the folder.

> `build.bat` uses the C# compiler that comes with Windows, so you don't need Visual Studio.

---

## Part 2: Put it on every lab PC

1. Copy **`CiscoReset.exe`** to each lab PC, for example to `C:\CiscoReset\CiscoReset.exe`. It's the only file needed.
2. *(Optional)* Right-click it and choose **Send to > Desktop (create shortcut)**.

It does **not** need administrator rights. Its settings and logs are kept next to the `.exe` (or in `%LOCALAPPDATA%\CiscoReset` if that folder is read-only).

> If Windows SmartScreen shows *"Windows protected your PC"*, click **More info**, then **Run anyway**. This happens because you built the app yourself, so it isn't signed.

**The devices must be reachable over Telnet**, which they are once the lab's *Basic Layer 3 configuration* is done (a `vty` password, an IP on VLAN 1, and so on). See [Troubleshooting](#troubleshooting) if one isn't.

---

## Part 3: How to use Cisco Reset, step by step

Do this on the student's PC.

### Step 1: Open Cisco Reset

Double-click **`CiscoReset.exe`**. The window has four parts:

| # | Section | What it's for |
|---|---|---|
| **1** | **This PC** | The PC number, found from the Wi-Fi IP. |
| **2** | **Cisco devices** | One tile per device: CUCM, coreTAAS, coreBABA. |
| **3** | **Reset** | The **Reset ticked devices...** button, and **Stop**. |
| | **Log** | Everything that happened, including what each device answered. |

### Step 2: Check the PC number

Section 1 shows **PC number: 12** and *from Wi-Fi IP 10.3.3.112*. The tiles show the addresses for that number (`10.12.100.8`, `10.12.1.2`, `10.12.1.4`).

- If it's wrong, or it says **Unknown**, type the right number in the box. It is remembered on this PC.
- **Detect from Wi-Fi** goes back to the number from the Wi-Fi IP.

### Step 3: Tick the devices to reset

All three are ticked. Click a tile to untick it (unticked tiles look faded). **Tick all** / **Tick none** do all three.

### Step 4 (optional): Click "Test connection"

Each tile turns **green** (reachable) or **gray** (not reachable). Nothing is changed on the devices.

### Step 5: Click "Reset ticked devices..."

A **Confirm reset** window lists each device, its address and what will be typed. Check that it's the right PC number, then click **Yes**.

Cisco Reset then works through the devices **one at a time, in order: CUCM, coreTAAS, coreBABA**. coreBABA goes last because this PC reaches CUCM *through* coreBABA.

- The tile that's working is **blue with moving stripes** and shows its progress.
- **Stop** stops after the current step. A device that was already erased but not reloaded is reported in the log.

### Step 6: Check the results

- **Green ✓**: erased and reloading.
- **Red ✗**: something went wrong. The tile and the log say what, and say whether anything was erased.

The devices take a few minutes to restart. When they come back, they're blank (`Switch>` / `Router>`), ready for the next class.

> ⚠️ After a reset, Telnet doesn't work on that device any more, because its IP address and `vty` password were erased too. Set it up again with the console cable, as at the start of the lab.

---

## What the tile colours mean

| Tile | Meaning |
|---|---|
| **Light gray, "Ready"** | Ticked, nothing running yet. |
| **Light blue, dots** | Waiting for its turn. |
| **Blue, moving stripes** | Working (with % done). |
| **Green ✓** | Done (or, after **Test connection**: reachable). |
| **Red ✗** | Problem. Read the tile or the log. |
| **Gray –** | Not reachable, or stopped before it started. |
| **Faded** | Not ticked. Click the tile to tick it. |

---

## Troubleshooting

| Problem | Fix |
|---|---|
| **PC number: Unknown** | The PC has no Wi-Fi IP. Type its number in section 1. |
| **Can't reach 10.m.x.x** | The device is off, not cabled, or has no IP yet. Check with `ping`. |
| **CUCM can't be reached, but the switches can** | This PC reaches CUCM through coreBABA. The PC needs its route (`route add 10.0.0.0 mask 255.0.0.0 10.m.1.4`, which is lost when the PC restarts) and CUCM needs its default route (`ip route 0.0.0.0 0.0.0.0 10.m.100.4`). Or reset CUCM with the console cable. |
| **Wrong Telnet password / Wrong enable password** | Someone changed it. Use **Settings...** to type the right one, or reset that device with the console cable. |
| **Wrong username or password** | The device uses `login local` with another user. Change **Username** in **Settings...**. |
| **Telnet login isn't set up on this device** | The device has no `vty` password, so it can't be reached over Telnet. Use the console cable. |
| **"... looks like PC 21's device, not PC 12's"** | The address reached a device named for another PC. Check the PC number in section 1. See [Safety rules](#safety-rules-built-in). |
| **vlan.dat could not be deleted** | The device was erased and reloaded, but `vlan.dat` is still there. Delete it by console: `delete vlan.dat`. |

---

## Safety rules (built in)

- **Nothing is erased until the login works.** A wrong password, a missing device or a timeout stops before `write erase`, and the message says *Nothing was erased*.
- **It never saves.** When `reload` asks *Save? [yes/no]*, it always answers **no**.
- **It checks the device's name.** If the device's hostname ends in a different PC number (for example `COREbaba-21` when this is PC 12), it refuses to reset it. Default names like `Switch` are fine. You can turn this off in **Settings...**.
- **It always asks first.** The confirm window lists every device and address.
- **Passwords are never written to the log.** The log shows `(password sent)`.

---

## Settings

Click **Settings...** to change:

| Setting | Default |
|---|---|
| Username (for `login local`) | `admin` |
| Telnet password | `pass` |
| Enable password | `pass` |
| CUCM address | `10.{m}.100.8` |
| coreTAAS address | `10.{m}.1.2` |
| coreBABA address | `10.{m}.1.4` |
| Name check | on |

`{m}` is replaced with the PC number. Telnet port 23 is used; add `:port` to an address to use another port.

The settings are saved in `CiscoReset.settings.ini` next to `CiscoReset.exe`, **in plain text**. That's fine for the lab default `pass`, but don't put a real password there.

---

## What's in the folder

| File | What it is |
|---|---|
| `CiscoReset.exe` | The app (made by `build.bat`). Copy it to every lab PC. |
| `CiscoReset.settings.ini` | Saved settings and the PC number (created automatically on each PC). |
| `Logs\` | One log file per day, with what each device answered. |
| `build.bat` + `src\` | Source code. Double-click `build.bat` to build `CiscoReset.exe`. |
| `test.bat` + `test\` | Automatic tests against pretend Cisco devices (no real switch needed). |

`.gitignore` keeps the `.exe`, the settings and the logs out of Git.

---

## Exactly what it types

For each ticked device, in order CUCM, coreTAAS, coreBABA, Cisco Reset opens a Telnet connection and waits for each prompt before answering:

| When the device shows | Cisco Reset sends |
|---|---|
| `Username:` | the username (`admin`) |
| `Password:` | the Telnet password (`pass`) |
| `COREbaba-12>` | `enable` |
| `Password:` (after `enable`) | the enable password (`pass`) |
| `COREbaba-12#` | `write erase` |
| `... Continue? [confirm]` | Enter |
| `COREbaba-12#` | `delete vlan.dat` *(switches only)* |
| `Delete filename [vlan.dat]?` | Enter |
| `Delete flash:/vlan.dat? [confirm]` | Enter |
| `COREbaba-12#` | `reload` |
| `Save? [yes/no]:` | `no` |
| `Proceed with reload? [confirm]` | Enter |

When the device drops the connection to restart, that device is done.

- If `vlan.dat` isn't there, that's fine: the log says so and it carries on.
- With `login local` (`username admin privilege 15`), the device goes straight to `#` and `enable` is skipped.
- If someone left the device in config mode (`(config)#`), it types `end` first.
