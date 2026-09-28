import { Button } from "@components/Button";
import { useDraggable } from "@dnd-kit/core";
import { CSS } from "@dnd-kit/utilities";
import { IconGripVertical, IconMinus, IconPlus, IconX } from "@tabler/icons-react";
import { PointerEvent, ReactNode } from "react";
import { SingleWidgetConfig } from "../../../atoms/widgets-atoms";

export type ResizeEdge = "left" | "right";

type Props = {
  children: ReactNode;
  className?: string;
  widgetName: string;
  widgetConfig: SingleWidgetConfig;
  onToggleMinimize: () => void;
  onRemove?: () => void;
  onResize?: (edge: ResizeEdge, clientX: number) => void;
  onResizeEnd?: () => void;
};

export const WidgetContainer = ({
  children,
  className = "",
  widgetName,
  widgetConfig,
  onToggleMinimize,
  onRemove,
  onResize,
  onResizeEnd,
}: Props) => {
  const { attributes, listeners, setNodeRef, transform, isDragging } = useDraggable({
    id: widgetConfig.id,
  });

  const style = {
    transform: transform ? CSS.Translate.toString(transform) : undefined,
  };

  return (
    <div
      ref={setNodeRef}
      style={style}
      className={`group relative rounded-lg border border-border h-full ${isDragging ? "z-20 opacity-80" : ""} ${className} ${
        widgetConfig.isMinimized ? "py-2" : ""
      }`}
    >
      {widgetConfig.isMinimized && (
        <span className="absolute -top-3 left-3 bg-background font-medium px-1">{widgetName}</span>
      )}

      <div className="absolute -top-3 right-3 bg-background px-1 flex gap-1 z-10">
        <Button
          variant="ghost"
          size="xs"
          className="hidden md:inline-flex h-6 w-6 rounded-full hover:bg-accent cursor-grab"
          {...attributes}
          {...listeners}
        >
          <IconGripVertical className="w-4 h-4" />
        </Button>
        {widgetConfig.isDefined && (
          <Button variant="ghost" size="xs" className="h-6 w-6 rounded-full hover:bg-accent" onClick={onToggleMinimize}>
            {widgetConfig.isMinimized ? <IconPlus className="w-4 h-4" /> : <IconMinus className="w-4 h-4" />}
          </Button>
        )}
        {widgetConfig.supportsRemove && widgetConfig.isDefined && (
          <Button variant="ghost" size="xs" className="h-6 w-6 rounded-full hover:bg-accent" onClick={onRemove}>
            <IconX className="w-4 h-4" />
          </Button>
        )}
      </div>
      {onResize && onResizeEnd && (
        <>
          <ResizeHandle edge="left" onResize={onResize} onResizeEnd={onResizeEnd} />
          <ResizeHandle edge="right" onResize={onResize} onResizeEnd={onResizeEnd} />
        </>
      )}
      <div className={`p-4 ${widgetConfig.isMinimized && widgetConfig.isDefined ? "hidden" : ""}`}>{children}</div>
    </div>
  );
};

function ResizeHandle(props: {
  edge: ResizeEdge;
  onResize: (edge: ResizeEdge, clientX: number) => void;
  onResizeEnd: () => void;
}) {
  const onPointerDown = (e: PointerEvent<HTMLDivElement>) => {
    e.preventDefault();
    e.stopPropagation();
    e.currentTarget.setPointerCapture(e.pointerId);
  };

  const onPointerMove = (e: PointerEvent<HTMLDivElement>) => {
    if (e.currentTarget.hasPointerCapture(e.pointerId)) {
      props.onResize(props.edge, e.clientX);
    }
  };

  const onPointerUp = (e: PointerEvent<HTMLDivElement>) => {
    if (e.currentTarget.hasPointerCapture(e.pointerId)) {
      e.currentTarget.releasePointerCapture(e.pointerId);
      props.onResizeEnd();
    }
  };

  return (
    <div
      onPointerDown={onPointerDown}
      onPointerMove={onPointerMove}
      onPointerUp={onPointerUp}
      onPointerCancel={onPointerUp}
      className={`hidden md:block absolute top-4 bottom-4 w-2 cursor-ew-resize rounded-full opacity-0 group-hover:opacity-100 hover:bg-primary/40 bg-border transition-opacity ${
        props.edge === "left" ? "-left-1" : "-right-1"
      }`}
    />
  );
}
