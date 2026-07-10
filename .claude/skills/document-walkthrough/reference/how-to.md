# How-to guide skeleton (task-oriented)

One guide per **real goal**. Starts and ends at a *meaningful* point — not a from-scratch re-teach.
Written from the user's goal, branches where the real world forks. One per goal; don't catalogue.

```text
# How to <achieve the goal>

> Goal: <the concrete result the user wants>. This assumes you've already <starting point — e.g.
> "installed X and created a base">; if not, do the tutorial first: <link>.

## Steps

1. <verb the action>

       <exact command / tool call>

   <observable result, if useful>

2. <next action>

   - **If <condition A>** → <do this>
   - **If <condition B>** → <do that>      ← branch where reality forks; don't assume one environment

3. …

## Result
You now have <the achieved result>. <Where to go next, if relevant.>

## See also
- <related how-to>
- Full options for these commands → <reference manual link>
```

## Rules (what makes a how-to pass)

- **From the user's goal, not the machinery.** The title is a task the user recognises ("How to import
  a folder of PDFs"), never a command name ("The `ingest` command").
- **Meaningful start and end.** Begin where a competent user actually is (state the assumptions, link the
  tutorial for setup) and stop when the goal is reached — don't re-teach the basics, don't run to
  exhaustive coverage.
- **Branch where the real world forks.** Use "If X → …, if Y → …". A how-to acknowledges messiness; it
  is *not* the single safe path a tutorial is.
- **Cover the common case fully; link the long tail.** Show the path most users need end-to-end; point to
  the reference manual for every flag/option.
- **Exact, observable steps.** One action per step, the real command, the result where it helps — same
  copy-paste-real bar as the tutorial.
- **One goal per guide.** If a "how-to" is teaching three goals, it's three guides.
