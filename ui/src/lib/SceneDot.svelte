<script>
  import { modifiedIds } from '../stores/sceneStore.js'
  import { showSceneChanges } from '../stores/uiState.js'

  export let slider = {}

  $: changed = $showSceneChanges && $modifiedIds.has(slider.sourceId ?? slider.id)
</script>

{#if changed}<span class="scene-dot" title="Differs from the active scene"></span>{/if}

<style>
  /* Inline with the control name, centred on its text line. */
  .scene-dot {
    display: inline-block;
    width: 5px;
    height: 5px;
    margin-left: 5px;
    vertical-align: middle;
    border-radius: 50%;
    background: var(--accent-light);
    opacity: 0.85;
  }
</style>
