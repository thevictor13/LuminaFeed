# Homework Plan

## The Plan

To complete my homework, I'll need to deliver a few things. It's not only the application itself and its source code (ideally finished, even if being rough around the edges), but all the specifications and prompts I write during the entirety of the process. This will enable you to gain insight into how my single-dev AI-native workflow works. Git commits will be fast-forward merged into main. I will be using worktrees to be able to run agents in parallel - although this will not be apparent on origin.

First of all, I decided to use the standard Blazor Webapp as a starting base, as this already implements registrations and sign-in. I will NOT be implementing a proper Database - I will rely on the provided SQLite solution, as this is beyond the scope of the Homework, especially considering time constraints.

Since this is already decided, the first step is to do some research on the currently available RSS feeds, and to create an initial set, including Categories. This will be seeded.

The UI will NOT be polished, there's no time for that.

Then I write a high level spec of the entire app, which gives a near-complete picture of what needs to be implemented. Normally I would not go too deep in this initial desciption, but due to the time constraints, I decided I'll go a bit deeper on implementation details, so that I can spend less time writing feature specs later on.

All the generated implementation plans will be committed in the plans forder, and I will extract all my communication with Claude from the cached JSONL files, into a more human-digestable format.

## What happened

Under the time constraint of 24 hours and the ambitious plan to have a robust solution, I came up with an idea I never did before: after processing my verbal spec of ~15 minutes (condensed), I was in the position to ask for a master plan: a plan of plans to be made, to arrive at the end result. This was a rather succesful approach, as this allowed me to parallelise several of the implementation planning and actual implementations, utilising worktrees.

I applied several review iterations after the implementation of each sections and tracks, which identified most gaps, bugs and inaccuracies. Beyond that, having a quick pass over the source code, my primary trust in the produced code will be given by the unit tests written, and the manual tests I'm conducting.

## Deliverables

- The source code
- The steps taken leading to the High-level spec in doc/initial
- All implementation plans in the docs/plans folder
- The prompt report, containing all my prompts and responses, comlete with a Gantt chart displaying session concurrency, in the tools/session-report folder
