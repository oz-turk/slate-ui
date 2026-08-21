<script>
  import SplitDivider from './SplitDivider.svelte'
  import Pane         from './Pane.svelte'

  export let node
</script>

{#if node.type === 'leaf'}
  <Pane paneId={node.paneId} />
{:else}
  <div class="split split-{node.dir}">
    <div class="child" style="flex: 0 1 {node.sizeA ?? 260}px">
      <svelte:self node={node.a} />
    </div>
    <SplitDivider dir={node.dir} splitId={node.splitId} />
    <div class="child" style="flex: 1 1 0">
      <svelte:self node={node.b} />
    </div>
  </div>
{/if}

<style>
  .split {
    display: flex;
    width: 100%;
    height: 100%;
    overflow: hidden;
  }
  .split-h { flex-direction: row; }
  .split-v { flex-direction: column; }

  .child {
    min-width:  0;
    min-height: 0;
    overflow:   hidden;
    display:    flex;
    flex-direction: column;
  }
</style>
