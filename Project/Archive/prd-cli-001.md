# PRD: Human Agent Fallback

## Feature Name
Human Agent Fallback via CLI

---

## Objective
Allow agents to request human input when they encounter a task they cannot handle autonomously. This ensures the agent workflow continues instead of failing or stalling.

---

## User Story
> As an agent, when I cannot confidently process a task, I want to delegate it to a human via the CLI so that the human can provide guidance or a direct answer.

---

## Requirements
1. **HumanAgentAdapter**
   - A special type of agent that prompts the user in the terminal.
   - Accepts a question or task description and waits for input from the user.
   - Returns the user’s input as the result of the task.

2. **Integration**
   - Agents must be able to detect when they're "stuck" (e.g., unhandled prompt, confidence too low).
   - When stuck, they should call `Spawn("human")` or similar via the runtime adapter.

3. **CLI Interaction**
   - Print the task to the terminal clearly.
   - Wait for multiline input until the user finishes (e.g., hits `Enter` twice or enters a special token like `::done`).
   - Return the input to the requesting agent as a normal response.

---

## Example Flow
```
🧠 Agent: I don’t know how to proceed with this task:
"Generate an OAuth2 token validator in Go with Redis cache."
👤 HumanAgent: Please enter your suggestion below (type "::done" to finish):
> [user types solution...]
> ::done
✅ Response received and passed back to original agent.
```

---

## Success Criteria
- Agents can delegate at runtime to a human agent.
- User sees a clear prompt and can respond interactively.
- Response flows back into the agent execution tree.
