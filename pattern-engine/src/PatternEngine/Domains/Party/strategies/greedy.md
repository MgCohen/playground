Greedy. For each quest, build one party step by step.

1. Start with the hero that makes the most rubrics of the quest true.
2. Ask for a grade of the party.
3. If the remainder is not empty, add the hero that makes the most rubrics of the remainder true. Ask for a grade again.
4. If the remainder is empty, commit that party.
5. If no hero in the catalog can make a rubric of the remainder true, commit null ("none") for that quest.
