# Tutorial — Gate your first package

**Who this is for:** someone who has never used Advisory. By the end you will have taken a real
open-source package all the way through the firewall — from looking it up, to having the firewall
**approve** it, to seeing it land where developers can pull it. One safe path, no choices. If anything
goes sideways, you can press **Reset test data** and start over with no harm.

**Time:** about 10 minutes.

**Before you start, you need:**
- The console address (`<CONSOLE_URL>`) — ask your administrator. This tutorial uses that placeholder.
- A modern web browser.
- Nothing installed. Everything happens in the browser.

> Throughout, `<CONSOLE_URL>` means the web address of your Advisory console. Your administrator set it
> up; it is **not** something you type literally.

---

## Step 1 — Open the console and sign in

Open `<CONSOLE_URL>` in your browser. You'll see the **splash login** — a split screen with a sign-in
panel on the left and a green capability panel on the right.

At the bottom of the sign-in panel is a **Require SSO sign-in** switch. For this tutorial, leave it
**off** (it shows a red "Testing mode" note — that's fine for learning).

Click **Continue**.

**You should see:** the console loads. The top bar shows the **Advisory** wordmark on the left, a
search box, **✦ Ask AI**, a green **Connected** dot, and **Policy v… · SHA-256 …** on the right. The
left side has a navigation menu with the groups **Catalog**, **Xray**, **Curation**, and **Pipeline**.
You start on the **Catalog** screen.

---

## Step 2 — Make sure an ecosystem is ready to gate

The firewall gates one "ecosystem" (package world) at a time, and an ecosystem must be **provisioned**
before you can send packages through it. We'll confirm **PyPI** (the Python package world) is ready.

1. In the left nav, under **Xray**, click **Scans List**.
2. At the top, click the **Repositories** tab.

**You should see:** an **Ecosystem firewall** panel with cards for each ecosystem. Find the **PyPI**
card.

- If the PyPI card shows a green dot and a **Remove** button, it's already provisioned — good, skip to
  Step 3.
- If it shows an **Add** button, click **Add** and wait a few seconds. The card switches to show it's
  provisioned.

**You should see:** below the cards, a **Repositories** table listing `pypi-quarantine` (type *proxy*)
and `pypi-approved` (type *hosted*). These are the two repos the firewall uses: packages land in
*quarantine* first, and approved ones move to *approved*.

---

## Step 3 — Look up a package in the Catalog

Now find a real package to gate. We'll use **six**, a tiny, long-stable Python package with no known
vulnerabilities — so the firewall will cleanly approve it.

1. In the left nav, click **Catalog** (top of the menu).
2. In the ecosystem dropdown next to the search box, choose **PyPI**.
3. In the search box, type `six`.
4. Click **Search**.
5. In the results, click the **six** package.

**You should see:** the package overview page for **six**. Near the top is an **approval banner**. For
a clean package it reads **✓ Approved for downloading** (green). You also see its published date,
number of versions, a vulnerabilities count (0), its licence, and **Install instructions**.

> This banner is the firewall **previewing** its verdict — before you've even sent the package through.

---

## Step 4 — Send the package into the firewall

1. On the **six** overview, find the **Send to Intake queue** button (in the left info card) and
   click it.

**You should see:** a confirmation that it was **✓ Sent to Intake queue**, with a note telling you to
watch it under **Pipeline → Quarantine**.

What just happened: the package was handed to the firewall's work queue. A background worker will fetch
it into the PyPI quarantine and the gate will evaluate it on its next cycle.

---

## Step 5 — Watch it move through the queue

1. In the left nav, under **Pipeline**, click **Intake queue**.

**You should see:** three counters — **Pending**, **Processed**, **Dead-lettered** — and an explainer
titled *"How a package moves through the firewall."* Right after you submitted, **Pending** may briefly
show 1, then **Processed** ticks up.

> **The firewall re-checks quarantine every 30 seconds.** If you just submitted, give it up to half a
> minute. A package added mid-cycle waits for the next tick — that's normal.

---

## Step 6 — See the gate's decision in Quarantine

1. In the left nav, under **Pipeline**, click **Quarantine**.

**You should see:** a table of packages the firewall is handling. Find **six**. Because it's
clean, its **Status** becomes **Promoting…** and then **Promoted**, with the reason *"Allowed —
promoted to approved."*

That's the win: the firewall **evaluated six against your policy and allowed it.**

> If it still says **Promoting…**, wait a few seconds and the page will update to **Promoted** — it's
> copying the package into the approved repo.

---

## Step 7 — Confirm it reached Approved packages

1. In the left nav, under **Pipeline**, click **Approved packages**.

**You should see:** **six** in the list of vetted packages — the ones developers are now allowed
to pull. Each row has a **Revoke** button (you won't use it now).

**This is your first success.** You took a real package from a Catalog search, sent it through the
firewall, and the gate **approved and promoted** it to the place developers consume from — with no
manual approval needed, because it passed your policy.

---

## Step 8 — See the evidence

Every decision is recorded. Let's look.

1. In the left nav, under **Pipeline**, click **Decision ledger**.

**You should see:** a row for **PyPI:six@…** with the decision **ALLOW**. Click the row to expand
it — you'll see **Why this decision** (a plain-English explanation), the **triggered controls**, and
the **source coverage** (which intelligence feeds were consulted).

---

## Recap

You just:
- Signed in to the console.
- Confirmed the PyPI ecosystem was provisioned (quarantine + approved repos).
- Looked up **six** in the Catalog and saw the gate's preview verdict.
- Sent it to the Intake queue.
- Watched it gate (every-30-seconds cycle) and get **Promoted**.
- Confirmed it in **Approved packages**.
- Read the decision in the **Decision ledger**.

That is the whole core loop of the firewall: **research → gate → quarantine → approve → evidence.**

## Where to go next

- **Try a package that gets blocked.** Search the Catalog for an older, vulnerable version (e.g.
  `PyYAML` version `3.10`) and send it through — its Quarantine status will be **Held / Blocked** with a
  reason. This shows the firewall doing its job.
- **Tune what the firewall allows:** see *How-to — Set your firewall policy*.
- **Let a developer actually pull `six`:** see *How-to — Pull an approved package*.
- **Start fresh anytime:** the **Reset test data** button (top bar) wipes the queue, quarantine,
  approved repos, and ledger clean so you can run this tutorial again.
