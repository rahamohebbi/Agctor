# Class Diagram

![Class Diagram](./class-diagram.jpg)

[Edit source](./class-diagram.mmd)

## Overview

Controllers, services, and background workers in the Host project.

## Controllers
- **AgentsController**: Agent CRUD + messaging via MessageDispatcher
- **GoalsController**: Goal CRUD via IGoalStore
- **ToolsController**: Tool invocation via ToolInvoker
- **TestController**: Scenario setup via IScenarioFactory

## Services
- **MessageDispatcher**: Routes messages through actor runtime
- **ToolInvoker**: Direct tool execution
- **ScenarioFactory**: Creates test scenarios

## Background Services
- **TaskScoperHostedService**: Goal-to-task decomposition
- **TaskFlowHostedService**: Task DAG execution
