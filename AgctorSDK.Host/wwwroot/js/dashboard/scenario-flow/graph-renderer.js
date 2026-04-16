/**
 * PRD-014: factory for the graph renderer. Swap implementation without changing callers.
 */
(function (global) {
    global.AgctorScenarioFlow = global.AgctorScenarioFlow || {};
    /**
     * @returns {{ mount: Function, read: Function, onChange: Function, destroy: Function, getCy?: Function, connectSelected?: Function, removeSelectedEdges?: Function, fitViewport?: Function }}
     */
    global.AgctorScenarioFlow.createGraphRenderer = function () {
        if (typeof global.AgctorScenarioFlow.createCytoscapeRenderer === 'function') {
            return global.AgctorScenarioFlow.createCytoscapeRenderer();
        }
        throw new Error('No graph renderer implementation registered.');
    };
})(typeof window !== 'undefined' ? window : globalThis);
