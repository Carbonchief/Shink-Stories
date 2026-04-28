const MIN_COLUMN_WIDTH = 88;
const MAX_COLUMN_WIDTH = 720;
const DESKTOP_MEDIA_QUERY = "(min-width: 900px)";

export function enhanceSubscriberGrid(gridSelector, storageKey, resizeLabel, autofitTitle) {
  const grid = document.querySelector(gridSelector);
  if (!(grid instanceof HTMLElement)) {
    return false;
  }

  const table = grid.querySelector("table");
  if (!(table instanceof HTMLTableElement) || !table.tHead || table.tHead.rows.length === 0) {
    return false;
  }

  const headers = Array.from(table.tHead.rows[0].cells).filter(
    (cell) => cell instanceof HTMLElement
  );

  if (headers.length === 0) {
    return false;
  }

  if (!window.matchMedia(DESKTOP_MEDIA_QUERY).matches) {
    grid.classList.remove("is-column-resizing");
    return false;
  }

  applyStoredWidths(table, storageKey, headers.length);

  headers.forEach((header, columnIndex) => {
    if (!(header instanceof HTMLElement)) {
      return;
    }

    header.dataset.columnIndex = String(columnIndex);

    let handle = header.querySelector(".admin-grid-resize-handle");
    if (!(handle instanceof HTMLElement)) {
      handle = document.createElement("span");
      handle.className = "admin-grid-resize-handle";
      handle.setAttribute("role", "separator");
      handle.setAttribute("aria-orientation", "vertical");
      header.appendChild(handle);
    }

    if (typeof resizeLabel === "string" && resizeLabel.trim().length > 0) {
      handle.setAttribute("aria-label", resizeLabel.trim());
    }

    if (typeof autofitTitle === "string" && autofitTitle.trim().length > 0) {
      handle.setAttribute("title", autofitTitle.trim());
    }

    if (handle.dataset.bound === "true") {
      return;
    }

    handle.dataset.bound = "true";

    handle.addEventListener("pointerdown", (event) => {
      beginResize(event, grid, table, columnIndex, storageKey);
    });

    handle.addEventListener("dblclick", (event) => {
      event.preventDefault();
      event.stopPropagation();
      autoFitColumn(grid, table, columnIndex, storageKey);
    });

    handle.addEventListener("click", (event) => {
      event.preventDefault();
      event.stopPropagation();
    });
  });

  return true;
}

export function downloadCsv(filename, csvText) {
  const safeFilename =
    typeof filename === "string" && filename.trim().length > 0
      ? filename.trim()
      : "subscribers.csv";
  const blob = new Blob([csvText ?? ""], { type: "text/csv;charset=utf-8" });
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = safeFilename;
  link.style.display = "none";
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}

function beginResize(event, grid, table, columnIndex, storageKey) {
  if (!(event.target instanceof HTMLElement)) {
    return;
  }

  event.preventDefault();
  event.stopPropagation();

  const handle = event.target;
  const header = table.tHead?.rows?.[0]?.cells?.[columnIndex];
  if (!(header instanceof HTMLElement)) {
    return;
  }

  const startX = event.clientX;
  const startWidth = header.getBoundingClientRect().width;

  handle.classList.add("is-active");
  grid.classList.add("is-column-resizing");

  const moveListener = (moveEvent) => {
    const delta = moveEvent.clientX - startX;
    const nextWidth = clampWidth(startWidth + delta);
    applyColumnWidth(table, columnIndex, nextWidth);
  };

  const stopListener = () => {
    handle.classList.remove("is-active");
    grid.classList.remove("is-column-resizing");
    persistCurrentWidths(table, storageKey);
    window.removeEventListener("pointermove", moveListener);
    window.removeEventListener("pointerup", stopListener);
    window.removeEventListener("pointercancel", stopListener);
  };

  window.addEventListener("pointermove", moveListener);
  window.addEventListener("pointerup", stopListener);
  window.addEventListener("pointercancel", stopListener);
}

function autoFitColumn(grid, table, columnIndex, storageKey) {
  const measuredWidth = measureAutoFitWidth(grid, table, columnIndex);
  applyColumnWidth(table, columnIndex, measuredWidth);
  persistCurrentWidths(table, storageKey);
}

function measureAutoFitWidth(grid, table, columnIndex) {
  const cells = Array.from(table.rows)
    .map((row) => row.cells[columnIndex])
    .filter((cell) => cell instanceof HTMLElement);

  if (cells.length === 0) {
    return MIN_COLUMN_WIDTH;
  }

  const probe = document.createElement("div");
  probe.style.position = "fixed";
  probe.style.top = "-10000px";
  probe.style.left = "-10000px";
  probe.style.visibility = "hidden";
  probe.style.pointerEvents = "none";
  probe.style.whiteSpace = "nowrap";
  probe.style.width = "auto";
  probe.style.maxWidth = "none";

  document.body.appendChild(probe);

  let maxWidth = MIN_COLUMN_WIDTH;

  try {
    for (const cell of cells) {
      probe.innerHTML = "";

      const clone = cell.cloneNode(true);
      if (clone instanceof HTMLElement) {
        clone.style.width = "auto";
        clone.style.minWidth = "0";
        clone.style.maxWidth = "none";
        clone.style.whiteSpace = "nowrap";
        clone.style.overflow = "visible";
      }

      probe.appendChild(clone);
      maxWidth = Math.max(maxWidth, probe.getBoundingClientRect().width + 24);
    }
  } finally {
    probe.remove();
  }

  const containerWidth = grid.clientWidth || table.getBoundingClientRect().width || MAX_COLUMN_WIDTH;
  return clampWidth(Math.min(maxWidth, Math.max(MIN_COLUMN_WIDTH, containerWidth - 24)));
}

function applyStoredWidths(table, storageKey, expectedColumnCount) {
  const storedWidths = readStoredWidths(storageKey);
  if (!storedWidths || storedWidths.length !== expectedColumnCount) {
    return;
  }

  storedWidths.forEach((width, columnIndex) => {
    if (typeof width === "number" && Number.isFinite(width)) {
      applyColumnWidth(table, columnIndex, width);
    }
  });
}

function applyColumnWidth(table, columnIndex, width) {
  const resolvedWidth = `${Math.round(clampWidth(width))}px`;
  const rows = Array.from(table.rows);
  rows.forEach((row) => {
    const cell = row.cells[columnIndex];
    if (!(cell instanceof HTMLElement)) {
      return;
    }

    cell.style.width = resolvedWidth;
    cell.style.minWidth = resolvedWidth;
    cell.style.maxWidth = resolvedWidth;
  });
}

function persistCurrentWidths(table, storageKey) {
  if (!storageKey) {
    return;
  }

  const headerCells = table.tHead?.rows?.[0]?.cells;
  if (!headerCells) {
    return;
  }

  const widths = Array.from(headerCells).map((cell) =>
    cell instanceof HTMLElement ? Math.round(cell.getBoundingClientRect().width) : null
  );

  try {
    window.localStorage.setItem(storageKey, JSON.stringify(widths));
  } catch {
    // Ignore storage issues.
  }
}

function readStoredWidths(storageKey) {
  if (!storageKey) {
    return null;
  }

  try {
    const raw = window.localStorage.getItem(storageKey);
    if (!raw) {
      return null;
    }

    const parsed = JSON.parse(raw);
    return Array.isArray(parsed) ? parsed : null;
  } catch {
    return null;
  }
}

function clampWidth(width) {
  return Math.min(MAX_COLUMN_WIDTH, Math.max(MIN_COLUMN_WIDTH, width));
}
