<script>
  import { showParamIcons, iconKeys, iconImages } from '../stores/uiState.js'
  import { postToCs } from './ipc.js'

  export let slider = {}
  export let mode   = 'preview'

  $: sourceId = slider.sourceId ?? slider.id
  $: src = $showParamIcons ? $iconImages[$iconKeys[sourceId]] : null

  // Preview mode only: single click jumps to the original object. Edit mode
  // keeps the icon purely informational so it can't fight row selection.
  function onClick(e) {
    if (mode !== 'preview') return
    e.stopPropagation()
    postToCs({ type: 'goto_request', id: sourceId })
  }
</script>

{#if src}
  <!-- svelte-ignore a11y-click-events-have-key-events -->
  <!-- svelte-ignore a11y-no-noninteractive-element-interactions -->
  <img class="param-icon" class:clickable={mode === 'preview'} {src} alt="" draggable="false"
       title={mode === 'preview' ? 'Go to in Grasshopper' : ''} on:click={onClick} />
{/if}

<style>
  .param-icon {
    width: 16px;
    height: 16px;
    vertical-align: -3px;
    margin-right: 5px;
  }
  .param-icon.clickable { cursor: pointer; }

  /* Light theme: GH's icons are drawn for GH's own canvas — hard black
     outlines and white fills that look heavy on the paper-toned pane.
     multiply lets the white fills take the pane colour, the reduced opacity
     lets the black outline blend toward it instead of staying pure black. */
  /* Dark theme: the UI never uses pure black (--bg #1a1a1a, --panel-bg
     #303030), so pull the icons' pure black/white toward the same range —
     contrast 0.75 maps black to ~#202020 and white to ~#dfdfdf. */
  :global(:root[data-theme="dark"]) .param-icon {
    filter: contrast(0.75);
  }
  :global(:root[data-theme="light"]) .param-icon {
    mix-blend-mode: multiply;
    opacity: 0.8;
  }
</style>
