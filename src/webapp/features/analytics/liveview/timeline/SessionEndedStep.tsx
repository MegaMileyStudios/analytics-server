import { IconPlayerStop } from "@tabler/icons-react";
import { SessionEndReason } from "./session-end";

type Props = {
  reason: SessionEndReason;
};

export function SessionEndedStep(props: Props) {
  return (
    <div className="relative">
      <div className="relative flex items-center space-x-2">
        <IconPlayerStop className="p-1.5 h-8 w-8 bg-muted border rounded-full" />
        <span className="text-xs text-muted-foreground">
          {props.reason === "shutdown" ? "Session ended" : "Session closed — no activity for over an hour"}
        </span>
      </div>
    </div>
  );
}
